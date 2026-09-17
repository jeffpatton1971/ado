# Dependency review

Reviewed 2026-09-17 before foundation delivery. Exact versions are centralized in
Directory.Packages.props; packages.lock.json records resolved transitive dependencies.

| Package | License | Maintenance/platform assessment | Cost |
|---|---|---|---|
| System.CommandLine 2.0.12 | MIT | Microsoft dotnet project; current stable release, POSIX/Windows parsing; net8.0 asset selected for net10.0 | No transitive runtime dependencies on this target |
| MSTest.TestFramework 4.4.1 | MIT | Microsoft-supported testfx project; modern .NET targets | Development only; includes analyzers |
| MSTest.TestAdapter 4.4.1 | MIT | Same maintained testfx project | Development-only test platform dependencies |
| Microsoft.NET.Test.Sdk 18.10.1 | MIT | Microsoft test platform | Development-only runner/object model/coverage dependencies |

License and dependency metadata are checked from the restored official NuGet packages.
No credentials or HTTP operations are delegated to a dependency. Native OS credential
integration and its licensing review remain pending. No Native AOT claim is made.

Security gate: `dotnet list Ado.slnx package --vulnerable --include-transitive`, NuGet
audit for all dependencies on restore, and committed lock files. An advisory scan is
not a guarantee of absence of vulnerabilities. Repeat on dependency upgrades and CI.

Sources: [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/2.0.12),
[MSTest](https://www.nuget.org/packages/MSTest.TestFramework/4.4.1),
[testfx license](https://github.com/microsoft/testfx/blob/main/LICENSE).
