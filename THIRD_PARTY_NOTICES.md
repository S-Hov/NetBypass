# Third-Party Notices

NetBypass does not bundle external Anti-DPI binaries in its application package.
It can download pinned official engine releases on explicit user request and
verifies them before use.

## Downloaded External Engines

- GoodbyeDPI by ValdikSS — Apache-2.0 license. Downloaded from the official
  GitHub release selected by the NetBypass adapter.
- zapret2 by bol-van — MIT license, copyright bol-van 2016–2026. NetBypass pins
  official zapret2 v1.0.3 and verifies the archive plus critical Windows binary,
  driver and Lua file SHA-256 checksums before activating it.

## Planned External Engines

The following projects are not bundled in v1.0.0:

- ByeDPI / ByeByeDPI family — license must be checked for the exact project and
  binary used.

## Runtime Platform

NetBypass is built with .NET and Avalonia. Release artifacts may include Microsoft
.NET runtime components when published as self-contained Windows builds.
