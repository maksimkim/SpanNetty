# SpanNetty — Project Description & Instructions for Future Code Changes

## 1. Project Overview

**SpanNetty** is a high-performance, asynchronous, event-driven network application framework for .NET. It is a fork of [DotNetty](https://github.com/azure/dotnetty) (Microsoft's C# port of Java's [Netty](https://github.com/netty/netty)), enhanced with modern .NET APIs (`Span<byte>`, `Memory<byte>`, `IBufferWriter<byte>`), array-pooled byte buffers, and an HTTP/2 codec.

- **Repository**: `c:\dev\ms\SpanNetty` (GitHub: https://github.com/maksimkim/SpanNetty)
- **Author/Publisher**: Microsoft (NuGet packages prefixed `Microsoft.Azure.SpanNetty.*`)
- **License**: MIT (with Apache 2.0 attribution to original Netty project)
- **Aligned with**: Netty 4.1.51.Final

---

## 2. Repository Layout

```
SpanNetty/
├── src/                        # Source projects (11 libraries)
│   ├── DotNetty.Common/        # Core utilities, threading, logging, platform
│   ├── DotNetty.Buffers/       # Buffer management (IByteBuffer, pooling, Span/Memory)
│   ├── DotNetty.Transport/     # Channel abstraction, event loops, socket I/O
│   ├── DotNetty.Transport.Libuv/ # Libuv-based transport (.NET Fx only)
│   ├── DotNetty.Codecs/        # Base codec infrastructure (encode/decode)
│   ├── DotNetty.Codecs.Http/   # HTTP/1.x codec
│   ├── DotNetty.Codecs.Http2/  # HTTP/2 codec
│   ├── DotNetty.Codecs.Mqtt/   # MQTT codec
│   ├── DotNetty.Codecs.Redis/  # Redis codec
│   ├── DotNetty.Codecs.Protobuf/ # Protocol Buffers codec
│   ├── DotNetty.Handlers/      # TLS, timeout, logging, flow-control handlers
│   ├── DotNetty.Handlers.Proxy/ # HTTP/SOCKS proxy handlers
│   ├── shared/                 # Shared source files linked by projects
│   ├── nuget.props              # NuGet package metadata (author, license, repo URL)
│   └── version.props            # Versioning scheme
├── test/                       # Test projects (~31 projects)
│   ├── DotNetty.*.Tests/       # Unit tests (xUnit 2.4.1 + Moq 4.16)
│   ├── DotNetty.*.Tests.Netstandard/ # Netstandard-targeted test variants
│   ├── DotNetty.End2End.Tests/ # Integration tests
│   ├── DotNetty.Suite.Tests/   # Suite tests
│   └── DotNetty.Tests.Common/  # Shared test infrastructure
├── examples/                   # Example applications (Echo, HTTP, WebSocket, etc.)
├── perf/                       # Benchmarks (DotNetty.Microbench, BenchmarkDotNet)
├── build/                      # CI/CD configs and dependency .props files
│   ├── pr-validation.yaml      # Azure Pipelines: PR validation (Win + Linux)
│   ├── pr-netfx-validation.yaml # Azure Pipelines: .NET Framework validation
│   ├── publish-packages.yaml   # Azure Pipelines: NuGet publish on git tag v*
│   ├── Dependencies.*.props    # Centralized package version management
│   └── templates/              # Reusable Azure Pipeline templates
├── shared/                     # Test certificates (contoso.com.pfx, dotnetty.com.pfx)
├── Directory.Build.props       # Root MSBuild properties (lang version, TFMs, signing)
├── Directory.Build.targets     # InformationalVersion assembly metadata
├── DotNetty.sln                # Full solution (74 projects)
├── DotNetty.Netstandard.sln    # Netstandard-focused subset (68 projects)
├── DotNetty.CrossPlatform.sln  # Cross-platform focused
├── DotNetty.Examples.sln       # Examples only
├── NuGet.Config                # NuGet sources (Azure Artifacts: ApiManagement feed)
└── localBuild.cmd              # Quick local Debug build
```

---

## 3. Dependency / Layer Graph

```
DotNetty.Common
    └── DotNetty.Buffers
        └── DotNetty.Transport
            ├── DotNetty.Transport.Libuv
            └── DotNetty.Codecs
                ├── DotNetty.Codecs.Http
                │   └── DotNetty.Codecs.Http2
                ├── DotNetty.Codecs.Mqtt
                ├── DotNetty.Codecs.Redis
                └── DotNetty.Codecs.Protobuf
DotNetty.Transport + DotNetty.Codecs + DotNetty.Transport.Libuv
    └── DotNetty.Handlers
        └── DotNetty.Handlers.Proxy (depends on DotNetty.Codecs.Http)
```

---

## 4. Target Frameworks

| Project | net471 | netstandard2.1 | net6.0 | net8.0 | net9.0 |
|---------|--------|----------------|--------|--------|--------|
| DotNetty.Common | ✓ (Win) | ✓ | ✓ | ✓ | ✓ |
| DotNetty.Buffers | ✓ (Win) | ✓ | ✓ | ✓ | ✓ |
| DotNetty.Transport | ✓ (Win) | ✓ | ✓ | ✓ | ✓ |
| DotNetty.Transport.Libuv | ✓ + net48 | — | — | — | — |
| DotNetty.Codecs | ✓ + net48 | ✓ | — | — | — |
| DotNetty.Codecs.Http | ✓ + net48 | ✓ | — | — | — |
| DotNetty.Codecs.Http2 | ✓ | ✓ | — | — | — |
| DotNetty.Codecs.Mqtt | ✓ + net48 | — | — | — | — |
| DotNetty.Codecs.Redis | ✓ + net48 | — | — | — | — |
| DotNetty.Codecs.Protobuf | ✓ + net48 | — | — | — | — |
| DotNetty.Handlers | ✓ (Win) | ✓ | ✓ | ✓ | ✓ |
| DotNetty.Handlers.Proxy | ✓ + net48 | — | — | — | — |

**Test frameworks**: net6.0, net8.0, net9.0, net471 (Win only)
**Example frameworks**: net9.0

---

## 5. Build & Development

### Build Commands

```powershell
# Quick local debug build (recommended for development)
.\localBuild.cmd

# Full build using FAKE orchestration
.\build.cmd Build              # Build only
.\build.cmd RunTests           # Build + run tests on net8.0
.\build.cmd RunTestsNetFx471   # Build + run tests on .NET Framework 4.7.1
.\build.cmd All                # Full build + all tests

# Cross-platform
./build.sh RunTests            # Linux/macOS

# Restore only
.\localRestore.cmd

# Publish packages locally
.\localPublish.cmd
```

### Compilation Settings (Directory.Build.props)

| Setting | Value |
|---------|-------|
| C# Language Version | **11.0** |
| Platform | AnyCPU |
| Tiered Compilation | Enabled |
| Treat Warnings as Errors | **false** |
| XML Documentation | Generated |
| Assembly Signing | Enabled (DotNetty.snk) |
| No .editorconfig | Style not enforced via config |

### Conditional Compilation Defines

| Target | Defines |
|--------|---------|
| .NETCoreApp | `NET_4_0_GREATER`, `NET_4_5_GREATER`, `NET_4_6_GREATER` |
| netstandard2.0 | `NETSTANDARD`, `NET_4_0_GREATER`, `NET_4_5_GREATER`, `NET_4_6_GREATER` |
| netstandard2.1 | All above + `NETSTANDARD_2_0_GREATER` |
| net471/net47 | `DESKTOPCLR`, `NET_4_0_GREATER`, `NET_4_5_GREATER`, `NET_4_6_GREATER` |
| net48 | All above + `NET_4_7_GREATER` |
| All | `NET_3_5_GREATER`, `SIGNED` |

### NuGet Package Source

Configured in `NuGet.Config`:
```
https://pkgs.dev.azure.com/msazure/_packaging/ApiManagement/nuget/v3/index.json
```

---

## 6. CI/CD (Azure Pipelines)

| Pipeline | Trigger | What It Does |
|----------|---------|--------------|
| `pr-validation.yaml` | PRs to `main`, `release/*` | Windows build + tests (Win 2022, Ubuntu 24, Ubuntu 22) |
| `pr-netfx-validation.yaml` | PRs to `main`, `release/*` | .NET Framework 4.7.1 build + tests (Windows only) |
| `publish-packages.yaml` | Git tags `v*` | Build Release → Push `.symbols.nupkg` to Azure Artifacts |

---

## 7. Coding Conventions & Patterns

### 7.1 Naming

- **Namespaces**: `DotNetty.{Module}` (e.g., `DotNetty.Common`, `DotNetty.Buffers`)
- **Interfaces**: `I`-prefix (e.g., `IChannel`, `IByteBuffer`, `IEventLoop`)
- **Abstract classes**: `Abstract` prefix (e.g., `AbstractChannel`, `AbstractByteBuffer`)
- **Private fields**: underscore `_field` or `v_field` prefix
- **PascalCase** for public members; **camelCase** for local variables
- No `.editorconfig` — follow existing patterns in each file

### 7.2 File Organization

- **Partial classes** are used extensively to split large types across files:
  - `AbstractChannel.cs` + `AbstractChannel.Unsafe.cs`
  - `PooledByteBuffer.cs` split across multiple files
- **Platform-specific files** use suffixes:
  - `.NetStandard.cs`, `.NetCore.cs`, `.NetFx.cs`
- **One interface or major class per file** (standard .NET convention)

### 7.3 ThrowHelper Pattern

All projects use a centralized `ThrowHelper` static partial class for exception throwing:

```csharp
internal static partial class ThrowHelper
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ThrowArgumentException()
    {
        throw GetArgumentException();
        static ArgumentException GetArgumentException() => new ArgumentException();
    }
}
```

- **`[MethodImpl(MethodImplOptions.NoInlining)]`** on every throw method (JIT performance)
- **Static local functions** to create exception objects
- **ExceptionArgument / ExceptionResource enums** for parameterized messages
- Each project has its own ThrowHelper extensions in `Internal\ThrowHelper.Extensions.cs`

### 7.4 Error Handling

- **Custom exception types** for specific failures (e.g., `ClosedChannelException`, `ConnectTimeoutException`)
- **Static singleton exceptions** reused for common cases (performance optimization)
- **Debug.Assert()** for internal invariant checking
- **Resource files** (`Strings.resx`) for localized error messages

### 7.5 Performance Patterns

- **ArrayPooled buffers** and **thread-local pools** (`ThreadLocalPool<T>`, `FastThreadLocal`)
- **Object recycling** via the Recycler pattern
- **`unsafe` code** allowed only in Common, Buffers, Codecs, and Codecs.Http2
- **`Span<byte>` / `Memory<byte>`** throughout buffer APIs
- **IBufferWriter<byte>** support on IByteBuffer

### 7.6 License Header

Every source file must include the tri-part license header:

```csharp
/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); ...
 *
 * Copyright (c) The DotNetty Project (Microsoft). All rights reserved.
 *   https://github.com/azure/dotnetty
 * Licensed under the MIT license. See LICENSE file in the project root ...
 *
 * Copyright (c) 2020 The Dotnetty-Span-Fork Project (cuteant@outlook.com)
 *   https://github.com/cuteant/dotnetty-span-fork
 * Licensed under the MIT license. See LICENSE file in the project root ...
 */
```

### 7.7 Test Conventions

- **Framework**: xUnit 2.4.1 with `Moq` for mocking
- **Shared infrastructure**: `DotNetty.Tests.Common` project
- **Naming**: Test classes mirror source classes with `Tests` suffix
- **Test config**: `xunit.runner.json` and `xunitSettings.props` in test root
- **Dual-target**: Tests run on both `net8.0` and `net471` (Windows)

---

## 8. Key Interfaces & Abstractions

| Interface | Location | Purpose |
|-----------|----------|---------|
| `IByteBuffer` | DotNetty.Buffers | Core buffer type with reader/writer indices, Span/Memory support |
| `IByteBufferAllocator` | DotNetty.Buffers | Factory for creating byte buffers (pooled/unpooled) |
| `IReferenceCounted` | DotNetty.Common | Reference counting for resource lifecycle management |
| `IChannel` | DotNetty.Transport | Network I/O abstraction (connect, read, write, close) |
| `IChannelHandler` | DotNetty.Transport | Handles I/O events in the channel pipeline |
| `IChannelPipeline` | DotNetty.Transport | Ordered chain of channel handlers |
| `IEventLoop` | DotNetty.Transport | Single-threaded event executor for a channel |
| `IEventLoopGroup` | DotNetty.Transport | Pool of event loops |

---

## 9. Versioning

- **Assembly Version**: Fixed at `1.0.0.0`
- **File Version**: Per-TFM (e.g., `1.0.0.2100` for net471, `1.0.0.9000` for modern .NET)
- **Package Version**: From `version.props` (default `1.0.0-beta`); overridden by git tag on publish
- **Debug builds**: Append date suffix (YYMMDDHHmm format)

---

## 10. Instructions for Making Code Changes

### Before You Start

1. **Build baseline**: Run `.\localBuild.cmd` to confirm the repo builds cleanly.
2. **Identify the layer**: Determine which project(s) your change touches. Respect the dependency graph — lower layers must not depend on higher ones.
3. **Check target frameworks**: Some projects only target netstandard2.1/net471 (e.g., Codecs.Http2). Ensure your code compiles for all listed TFMs.

### Making Changes

1. **Follow existing patterns** in the file you're editing. There is no `.editorconfig` — consistency with neighboring code is the rule.
2. **Use ThrowHelper** for throwing exceptions — never throw inline in hot paths.
3. **Conditional compilation**: If your change is TFM-specific, use the existing `#if` defines or the `.NetStandard.cs` / `.NetCore.cs` file suffix pattern.
4. **Unsafe code**: Only allowed in projects with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (Common, Buffers, Codecs, Codecs.Http2). Transport and Handlers do NOT allow unsafe.
5. **License headers**: Include the standard tri-part header in any new source file.
6. **Partial classes**: For large types, consider splitting into multiple partial class files.
7. **Localization**: Add error message strings to the project's `Strings.resx` when applicable.

### Testing Changes

```powershell
# Run all tests on .NET Core
.\build.cmd RunTests

# Run .NET Framework tests (Windows only)
.\build.cmd RunTestsNetFx471

# Run specific test project
dotnet test test\DotNetty.Buffers.Tests\DotNetty.Buffers.Tests.csproj -f net8.0

# Run with filter
dotnet test test\DotNetty.Transport.Tests\DotNetty.Transport.Tests.csproj -f net8.0 --filter "FullyQualifiedName~ChannelTest"
```

### Before Committing

1. Ensure `.\localBuild.cmd` succeeds (or `.\build.cmd Build` for Release).
2. Run relevant test projects.
3. Check that no new warnings are introduced (even though `TreatWarningsAsErrors` is false).
4. New public APIs should have XML documentation comments.

---

## 11. Package Publishing

Publishing is automated via `publish-packages.yaml`:
1. Create and push a git tag matching `v*` (e.g., `v1.2.3`)
2. Pipeline extracts version from tag, builds in Release, pushes `.symbols.nupkg` to Azure Artifacts
3. Published NuGet packages: `Microsoft.Azure.SpanNetty.*`
