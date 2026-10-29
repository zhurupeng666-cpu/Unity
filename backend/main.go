package main

import (
	"context"
	"crypto/rand"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"log"
	"math"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"slices"
	"strings"
	"sync"
	"syscall"
	"time"
)

type Position struct {
	X float64 `json:"x"`
	Y float64 `json:"y"`
	Z float64 `json:"z"`
}

type Issue struct {
	ID          string    `json:"id"`
	Title       string    `json:"title"`
	Description string    `json:"description"`
	Priority    string    `json:"priority"`
	Status      string    `json:"status"`
	Position    Position  `json:"position"`
	CreatedAt   time.Time `json:"createdAt"`
	UpdatedAt   time.Time `json:"updatedAt"`
}

type Store struct {
	mu     sync.RWMutex
	path   string
	issues []Issue
}

func openStore(path string) (*Store, error) {
	abs, err := filepath.Abs(path)
	if err != nil {
		return nil, err
	}
	s := &Store{path: abs, issues: []Issue{}}
	if err = os.MkdirAll(filepath.Dir(abs), 0750); err != nil {
		return nil, err
	}
	b, err := os.ReadFile(abs)
	if errors.Is(err, os.ErrNotExist) {
		return s, nil
	}
	if err != nil {
		return nil, err
	}
	if err = json.Unmarshal(b, &s.issues); err != nil {
		return nil, fmt.Errorf("invalid data file (not overwritten): %w", err)
	}
	seen := map[string]bool{}
	for _, v := range s.issues {
		if v.ID == "" || seen[v.ID] || strings.TrimSpace(v.Title) == "" || !validPriority(v.Priority) || !validStatus(v.Status) || v.CreatedAt.IsZero() || v.UpdatedAt.Before(v.CreatedAt) {
			return nil, fmt.Errorf("invalid saved issue: %q", v.ID)
		}
		seen[v.ID] = true
	}
	if s.issues == nil {
		s.issues = []Issue{}
	}
	return s, nil
}

// Persist a complete candidate snapshot before publishing it in memory.
// One server process owns the file; never run two writers on the same path.
func (s *Store) save(next []Issue) error {
	b, err := json.MarshalIndent(next, "", "  ")
	if err != nil {
		return err
	}
	f, err := os.CreateTemp(filepath.Dir(s.path), ".issues-*.tmp")
	if err != nil {
		return err
	}
	tmp := f.Name()
	defer os.Remove(tmp)
	if _, err = f.Write(b); err != nil {
		f.Close()
		return err
	}
	if err = f.Sync(); err != nil {
		f.Close()
		return err
	}
	if err = f.Close(); err != nil {
		return err
	}
	if err = os.Rename(tmp, s.path); err != nil {
		return err
	}
	s.issues = next
	return nil
}

func (s *Store) list() []Issue {
	s.mu.RLock()
	defer s.mu.RUnlock()
	result := append([]Issue{}, s.issues...)
	slices.SortFunc(result, func(a, b Issue) int { return b.CreatedAt.Compare(a.CreatedAt) })
	return result
}

func validPriority(v string) bool { return v == "low" || v == "medium" || v == "high" }
func validStatus(v string) bool   { return v == "open" || v == "in_progress" || v == "resolved" }

func respond(w http.ResponseWriter, code int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(code)
	_ = json.NewEncoder(w).Encode(v)
}

func fail(w http.ResponseWriter, code int, key, message string) {
	respond(w, code, map[string]any{"error": map[string]string{"code": key, "message": message}})
}

func decode(w http.ResponseWriter, r *http.Request, v any) bool {
	if strings.Split(r.Header.Get("Content-Type"), ";")[0] != "application/json" {
		fail(w, 415, "UNSUPPORTED_MEDIA_TYPE", "Content-Type must be application/json")
		return false
	}
	d := json.NewDecoder(http.MaxBytesReader(w, r.Body, 64*1024))
	d.DisallowUnknownFields()
	if err := d.Decode(v); err != nil {
		fail(w, 400, "INVALID_ARGUMENT", "invalid JSON, unknown field, or body exceeds 64 KiB")
		return false
	}
	if d.Decode(new(any)) != io.EOF {
		fail(w, 400, "INVALID_ARGUMENT", "request must contain one JSON object")
		return false
	}
	return true
}

func handler(s *Store, origins, webDir string) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /health", func(w http.ResponseWriter, r *http.Request) { respond(w, 200, map[string]string{"status": "ok"}) })
	mux.HandleFunc("GET /api/issues", func(w http.ResponseWriter, r *http.Request) { respond(w, 200, s.list()) })
	mux.HandleFunc("POST /api/issues", func(w http.ResponseWriter, r *http.Request) {
		var in struct {
			Title       string `json:"title"`
			Description string `json:"description"`
			Priority    string `json:"priority"`
			Position    *struct {
				X *float64 `json:"x"`
				Y *float64 `json:"y"`
				Z *float64 `json:"z"`
			} `json:"position"`
		}
		if !decode(w, r, &in) {
			return
		}
		in.Title = strings.TrimSpace(in.Title)
		if len([]rune(in.Title)) == 0 || len([]rune(in.Title)) > 120 {
			fail(w, 400, "INVALID_ARGUMENT", "title is required and must be at most 120 characters")
			return
		}
		if len([]rune(in.Description)) > 2000 {
			fail(w, 400, "INVALID_ARGUMENT", "description must be at most 2000 characters")
			return
		}
		if !validPriority(in.Priority) {
			fail(w, 400, "INVALID_ARGUMENT", "priority must be low, medium, or high")
			return
		}
		p := in.Position
		if p == nil || p.X == nil || p.Y == nil || p.Z == nil {
			fail(w, 400, "INVALID_ARGUMENT", "position.x/y/z are required numbers")
			return
		}
		for _, n := range []float64{*p.X, *p.Y, *p.Z} {
			if math.IsNaN(n) || math.IsInf(n, 0) {
				fail(w, 400, "INVALID_ARGUMENT", "position must be finite")
				return
			}
		}
		id := make([]byte, 16)
		if _, err := rand.Read(id); err != nil {
			fail(w, 500, "INTERNAL_ERROR", "could not generate ID")
			return
		}
		now := time.Now().UTC()
		issue := Issue{hex.EncodeToString(id), in.Title, in.Description, in.Priority, "open", Position{*p.X, *p.Y, *p.Z}, now, now}
		s.mu.Lock()
		err := s.save(append(append([]Issue{}, s.issues...), issue))
		s.mu.Unlock()
		if err != nil {
			log.Printf("save: %v", err)
			fail(w, 500, "STORAGE_ERROR", "could not save issue")
			return
		}
		respond(w, 201, issue)
	})
	mux.HandleFunc("PATCH /api/issues/{id}/status", func(w http.ResponseWriter, r *http.Request) {
		var in struct {
			Status string `json:"status"`
		}
		if !decode(w, r, &in) {
			return
		}
		if !validStatus(in.Status) {
			fail(w, 400, "INVALID_ARGUMENT", "status must be open, in_progress, or resolved")
			return
		}
		s.mu.Lock()
		defer s.mu.Unlock()
		next := append([]Issue{}, s.issues...)
		for i := range next {
			if next[i].ID != r.PathValue("id") {
				continue
			}
			next[i].Status = in.Status
			next[i].UpdatedAt = time.Now().UTC()
			if err := s.save(next); err != nil {
				log.Printf("save: %v", err)
				fail(w, 500, "STORAGE_ERROR", "could not save status")
				return
			}
			respond(w, 200, next[i])
			return
		}
		fail(w, 404, "NOT_FOUND", "issue not found")
	})
	mux.HandleFunc("/api/", func(w http.ResponseWriter, r *http.Request) { fail(w, 404, "NOT_FOUND", "API route not found") })
	if webDir != "" {
		files := http.FileServer(http.Dir(webDir))
		mux.Handle("/", http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			if r.Method != http.MethodGet && r.Method != http.MethodHead {
				fail(w, 405, "METHOD_NOT_ALLOWED", "static files only support GET and HEAD")
				return
			}
			files.ServeHTTP(w, r)
		}))
	}
	allowed := map[string]bool{}
	for _, origin := range strings.Split(origins, ",") {
		allowed[strings.TrimSpace(origin)] = true
	}
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		w.Header().Set("X-Content-Type-Options", "nosniff")
		if origin := r.Header.Get("Origin"); origin != "" {
			w.Header().Add("Vary", "Origin")
			if allowed[origin] {
				w.Header().Set("Access-Control-Allow-Origin", origin)
				w.Header().Set("Access-Control-Allow-Methods", "GET, POST, PATCH, OPTIONS")
				w.Header().Set("Access-Control-Allow-Headers", "Content-Type")
			}
		}
		if r.Method == http.MethodOptions {
			w.WriteHeader(204)
			return
		}
		mux.ServeHTTP(w, r)
	})
}

func main() {
	addr := flag.String("addr", ":8080", "listen address")
	data := flag.String("data", "data/issues.json", "persistent JSON path (one writer only)")
	origins := flag.String("origins", "http://localhost:5173,http://127.0.0.1:5173", "comma-separated browser origins")
	webDir := flag.String("web", "", "optional frontend dist directory")
	flag.Parse()
	s, err := openStore(*data)
	if err != nil {
		log.Fatal(err)
	}
	srv := &http.Server{Addr: *addr, Handler: handler(s, *origins, *webDir), ReadHeaderTimeout: 5 * time.Second, ReadTimeout: 15 * time.Second, WriteTimeout: 15 * time.Second, IdleTimeout: 60 * time.Second}
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	go func() {
		<-ctx.Done()
		shutdown, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		_ = srv.Shutdown(shutdown)
	}()
	log.Printf("listening %s; data=%s", *addr, s.path)
	if err := srv.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		log.Fatal(err)
	}
}
