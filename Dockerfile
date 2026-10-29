FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM golang:1.24.13-alpine AS backend
WORKDIR /src/backend
COPY backend/ ./
RUN CGO_ENABLED=0 go build -trimpath -o /inspection .

FROM alpine:3.22
RUN adduser -D app && mkdir /data && chown app:app /data
COPY --from=backend /inspection /app/inspection
COPY --from=frontend /src/frontend/dist /app/web
USER app
VOLUME ["/data"]
EXPOSE 8080
CMD ["/app/inspection", "-addr", ":8080", "-data", "/data/issues.json", "-web", "/app/web"]
