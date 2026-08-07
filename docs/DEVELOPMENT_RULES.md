# Development Rules

These rules keep Playbook professional, maintainable, and easy to evolve.

## 1. Single responsibility

Every class, service, and engine owns one clear job. If a type needs an "and" to describe itself, split it.

## 2. Small pull-request sized changes

Prefer incremental, reviewable changes. Large mixed PRs slow feedback and hide regressions.

## 3. Never break the build

The solution must always compile. Fix the build before continuing feature work. Run `dotnet build` and relevant tests before opening a PR.

## 4. Explainable code

Write code that another engineer can understand quickly. Prefer clear names, small methods, and explicit intent over cleverness.

## 5. Dependency injection everywhere

Compose behavior through the DI container. Do not new up infrastructure or application services deep in the call stack when they should be injected.

## 6. Avoid static state

No shared mutable statics for application behavior. Static helpers are acceptable only when they are pure and side-effect free.

## 7. Favor interfaces

Define contracts at the Core/Application boundary. Implement adapters in Infrastructure. This keeps engines swappable and easy to fake in tests.

## 8. Build incrementally

Ship thin vertical slices. Stub external systems when needed, but keep seams in place so real implementations can replace stubs without redesign.

## 9. Every feature should be testable

Design for testability from the start. Business logic belongs in Core/Application where xUnit can exercise it without the UI or a live database.

## 10. Every recommendation must eventually be explainable

Fantasy features are not in scope for the foundation, but when recommendations arrive they must expose action, confidence, impact, and reasoning. Opaque outputs are not acceptable.
