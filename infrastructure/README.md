# Infrastructure

This directory reserves a home for infrastructure configuration. No runnable Docker, Compose, or Kubernetes configuration is included in Phase 1.

As implementation progresses, use:

- `docker/` for container build definitions and local Compose services.
- `kubernetes/` for deployment manifests when deployment work begins.
- `helm/` for Helm packaging if the deployment approach requires it.

Create those subdirectories when they contain real configuration. Keep deployment secrets outside version control, document required variables alongside each configuration, and use local defaults only where they are safe. Do not use production credentials for local development.

The current API and frontend can run directly using the instructions in the [root README](../README.md).
