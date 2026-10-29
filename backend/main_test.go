package main

import (
	"bytes"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"sync"
	"testing"
)

const good = `{"title":"墙面破损","description":"裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}`

func request(h http.Handler, method, path, body string) *httptest.ResponseRecorder {
	r := httptest.NewRequest(method, path, bytes.NewBufferString(body))
	r.Header.Set("Content-Type", "application/json")
	w := httptest.NewRecorder()
	h.ServeHTTP(w, r)
	return w
}

func TestLifecycleAndRestart(t *testing.T) {
	path := filepath.Join(t.TempDir(), "issues.json")
	s, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	h := handler(s, "http://localhost:5173", "")
	if w := request(h, "GET", "/health", ""); w.Code != 200 {
		t.Fatal(w)
	}
	if w := request(h, "GET", "/api/issues", ""); w.Body.String() != "[]\n" {
		t.Fatal(w.Body.String())
	}
	w := request(h, "POST", "/api/issues", good)
	if w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	var issue Issue
	if err = json.Unmarshal(w.Body.Bytes(), &issue); err != nil {
		t.Fatal(err)
	}
	if len(issue.ID) != 32 || issue.Status != "open" || issue.CreatedAt.IsZero() {
		t.Fatal(issue)
	}
	for _, status := range []string{"in_progress", "resolved", "open"} {
		w = request(h, "PATCH", "/api/issues/"+issue.ID+"/status", `{"status":"`+status+`"}`)
		if w.Code != 200 {
			t.Fatal(w.Body.String())
		}
	}
	s2, err := openStore(path)
	if err != nil {
		t.Fatal(err)
	}
	got := s2.list()
	if len(got) != 1 || got[0].ID != issue.ID || got[0].Status != "open" || got[0].UpdatedAt.Before(issue.CreatedAt) {
		t.Fatal(got)
	}
}

func TestValidation(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "issues.json"))
	h := handler(s, "", "")
	for _, body := range []string{
		`null`, `{}`, `{"title":"  ","priority":"high"}`, `{"title":"ok","priority":"urgent"}`,
		`{"title":"ok","priority":"low","position":{"x":0,"y":0}}`,
		`{"title":"ok","priority":"low","position":{"x":null,"y":0,"z":0}}`,
		`{"title":"ok","priority":"low","position":{"x":1e999,"y":0,"z":0}}`,
		good + `{}`, `{"unexpected":true}`, `{"title":`,
	} {
		if w := request(h, "POST", "/api/issues", body); w.Code != 400 {
			t.Errorf("%s: %d", body, w.Code)
		}
	}
	if w := request(h, "PATCH", "/api/issues/none/status", `{"status":"bad"}`); w.Code != 400 {
		t.Fatal(w)
	}
	if w := request(h, "PATCH", "/api/issues/none/status", `{"status":"open"}`); w.Code != 404 {
		t.Fatal(w)
	}
	if len(s.list()) != 0 {
		t.Fatal("invalid input persisted")
	}
}

func TestConcurrentCreates(t *testing.T) {
	s, _ := openStore(filepath.Join(t.TempDir(), "issues.json"))
	h := handler(s, "", "")
	var wg sync.WaitGroup
	for i := 0; i < 30; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if w := request(h, "POST", "/api/issues", good); w.Code != 201 {
				t.Error(w.Code)
			}
		}()
	}
	wg.Wait()
	seen := map[string]bool{}
	for _, v := range s.list() {
		if seen[v.ID] {
			t.Fatal("duplicate ID")
		}
		seen[v.ID] = true
	}
	if len(seen) != 30 {
		t.Fatal(len(seen))
	}
	reloaded, err := openStore(s.path)
	if err != nil || len(reloaded.list()) != 30 {
		t.Fatal("concurrent writes lost", err)
	}
}

func TestFailedWriteDoesNotPublish(t *testing.T) {
	dir := t.TempDir()
	s, _ := openStore(filepath.Join(dir, "issues.json"))
	h := handler(s, "", "")
	w := request(h, "POST", "/api/issues", good)
	if w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	var issue Issue
	_ = json.Unmarshal(w.Body.Bytes(), &issue)
	// Point the destination at a directory: atomic rename must fail on all platforms.
	s.path = dir
	if w = request(h, "POST", "/api/issues", good); w.Code != 500 {
		t.Fatal(w.Code)
	}
	if w = request(h, "PATCH", fmt.Sprintf("/api/issues/%s/status", issue.ID), `{"status":"resolved"}`); w.Code != 500 {
		t.Fatal(w.Code)
	}
	if got := s.list(); len(got) != 1 || got[0].Status != "open" {
		t.Fatal("failed write leaked", got)
	}
}

func TestCorruptFileAndCORS(t *testing.T) {
	p := filepath.Join(t.TempDir(), "bad.json")
	_ = os.WriteFile(p, []byte("broken"), 0600)
	if _, err := openStore(p); err == nil {
		t.Fatal("corrupt file accepted")
	}
	s, _ := openStore(filepath.Join(t.TempDir(), "issues.json"))
	h := handler(s, "http://localhost:5173", "")
	for _, origin := range []string{"http://localhost:5173", "http://evil.invalid"} {
		r := httptest.NewRequest("OPTIONS", "/api/issues", nil)
		r.Header.Set("Origin", origin)
		w := httptest.NewRecorder()
		h.ServeHTTP(w, r)
		if w.Code != 204 {
			t.Fatal(w.Code)
		}
		want := ""
		if origin == "http://localhost:5173" {
			want = origin
		}
		if w.Header().Get("Access-Control-Allow-Origin") != want {
			t.Fatal(w.Header())
		}
	}
}

func TestStaticWebAndAPICoexist(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "index.html"), []byte("<h1>Inspection</h1>"), 0600); err != nil {
		t.Fatal(err)
	}
	s, err := openStore(filepath.Join(t.TempDir(), "issues.json"))
	if err != nil {
		t.Fatal(err)
	}
	h := handler(s, "", dir)
	if w := request(h, "GET", "/", ""); w.Code != 200 || w.Body.String() != "<h1>Inspection</h1>" {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "POST", "/api/issues", good); w.Code != 201 {
		t.Fatal(w.Code, w.Body.String())
	}
	if w := request(h, "GET", "/api/missing", ""); w.Code != 404 {
		t.Fatal(w.Code)
	}
	if w := request(h, "POST", "/", good); w.Code != 405 {
		t.Fatal(w.Code)
	}
}
