# GitHub repository conventions

This directory is reserved for GitHub configuration and contribution templates. Phase 1 does not add executable workflows or deployment automation.

Future workflows belong in `workflows/` and should restore and build the .NET solution, install frontend dependencies from the lockfile, lint and build the frontend, and run automated tests as test projects are introduced.

Keep workflow permissions scoped to the job's needs. Store future CI credentials in GitHub secrets or use an appropriate workload identity; never commit credentials in workflow YAML. Add deployment workflows only when deployment work is explicitly in scope.
