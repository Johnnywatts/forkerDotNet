# Multi-stage build for ForkerDotNet Console
# Stage 1: Build the Go binary
FROM golang:1.23-alpine AS builder

WORKDIR /build

# Copy Go module files and download dependencies
COPY go.mod go.sum ./
RUN go mod download

# Copy source code
COPY . .

# Build static binary (CGO_ENABLED=0 for pure Go)
# -ldflags="-s -w" strips debug info for smaller binary
RUN CGO_ENABLED=0 GOOS=linux GOARCH=amd64 go build \
    -ldflags="-s -w" \
    -o console \
    ./cmd/console

# Stage 2: Create minimal runtime image
FROM scratch

# Copy SSL certificates for HTTPS (if needed)
COPY --from=builder /etc/ssl/certs/ca-certificates.crt /etc/ssl/certs/

# Copy binary and web assets
COPY --from=builder /build/console /console
COPY --from=builder /build/web /web

# Expose HTTP port
EXPOSE 5000

# Run as non-root (UID 1000)
USER 1000:1000

# Start console
ENTRYPOINT ["/console"]
