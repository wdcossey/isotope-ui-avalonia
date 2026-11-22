## C co-touched areas in commits that touch E (window, 35 commits; CHANGELOG.md touched in 31)
| area | commits also touching it |
|---|---|
| T | 35 |
| X | 34 |
| D | 32 |
| O1 | 25 |
| W1 | 23 |
| W4 | 15 |
| W2 | 14 |
| S | 11 |
| W3 | 10 |

| engine area | commits | no other src code (no W*, O1, S) | % |
|---|---|---|---|
| E1 | 16 | 2 | 12% |
| E2 | 14 | 1 | 7% |
| E3 | 1 | 1 | 100% |
| E4 | 6 | 0 | 0% |
| E5 | 4 | 0 | 0% |
| E* | 35 | 4 | 11% |

## CL every window commit touching E
| date | commit | E areas | E lines | W lines | O1 lines | files | other src code areas | subject |
|---|---|---|---|---|---|---|---|---|
| 2026-08-26 | ffe1de4d | E1+E4 | 53 | 96 | 19 | 294 | O1+S+W1+W3+W4 | Guide audit: fix stale samples, phantom APIs, and the doc-pipeline gaps tha |
| 2026-08-27 | 1851f43b | E1 | 4 | 12 | 0 | 305 | S+W1+W2+W4 | Compile the guides' hand-typed C# instead of hoping it works (#1139) |
| 2026-08-31 | 27ed6662 | E4 | 11 | 0 | 74 | 15 | O1+S | Attribute thin helpers to their callers and stamp converted arguments (spec |
| 2026-08-31 | 33a35999 | E2 | 15 | 473 | 439 | 58 | O1+S+W1+W2+W3+W4 | Per-element source mapping via Roslyn interceptors (spec 010) (#1147) |
| 2026-09-28 | e5a3edc2 | E5 | 2082 | 804 | 0 | 27 | W1+W2+W4 | Fix validation context: `.Validate()` never ran outside `FormField` (#1270) |
| 2026-10-01 | 28ace24d | E2 | 147 | 119 | 16 | 15 | O1+W1+W3 | Don't let a reused or memoized panel child swallow a component's own update |
| 2026-10-06 | 00cd4892 | E1 | 47 | 920 | 567 | 29 | O1+W1+W2+W4 | Let apps replace the built-in render-error fallback (#1311) |
| 2026-10-07 | a4dcfdf5 | E1+E4+E5 | 18 | 24 | 22 | 58 | O1+S+W4 | Enable IDE0330 and move lock gates to System.Threading.Lock (#1351) |
| 2026-10-13 | 0f84736e | E1 | 30 | 887 | 590 | 44 | O1+S+W1+W4 | Inspector diagnostics: call-site column in SourceLocation + ComponentRender |
| 2026-10-19 | 05ecd1ce | E1 | 40 | 0 | 100 | 20 | O1 | Complete picker helpers for multiple-file selection and saving (#1372) |
| 2026-10-19 | 73328eba | E1 | 246 | 504 | 1282 | 33 | O1+S+W1+W2+W4 | Add text-only component inspection to ReactorDiagnostics (#1332) |
| 2026-10-19 | fb618d7e | E2 | 22 | 987 | 251 | 35 | O1+W1+W2+W4 | Hosts: ReactorHostControl idle wait; Theme.NotifyResourcesChanged() for run |
| 2026-10-21 | 262488ee | E2 | 21 | 859 | 1159 | 57 | O1+S+W1+W2+W3+W4 | Inspector diagnostics without a managed agent: ReactorSource on every contr |
| 2026-10-21 | 713ecc95 | E1 | 116 | 38 | 384 | 32 | O1+W1+W4 | Migrate picker helpers to Microsoft.Windows.Storage.Pickers (#1394) |
| 2026-10-22 | 2a519275 | E1 | 92 | 285 | 0 | 26 | W1+W2+W3+W4 | Resolve window hooks from the reconciler-supplied owner, not ActiveHostInte |
| 2026-10-22 | 5ba98bf7 | E1 | 85 | 91 | 35 | 19 | O1+W1+W3+W4 | Support picker methods in ReactorHostControl-hosted content (#1397) |
| 2026-10-27 | 16062a12 | E2 | 200 | 149 | 85 | 19 | O1+W1+W2+W3 | Keep a conditional child's slot among unkeyed siblings; unmount ListView ro |
| 2026-10-28 | 5555551b | E2 | 27 | 750 | 0 | 35 | S+W1+W2+W3 | Fix dynamic ItemsHost reconciliation for generated control wrappers (#1363) |
| 2026-10-28 | 84d1e805 | E2 | 330 | 683 | 223 | 50 | O1+S+W1+W2+W3+W4 | Fix diagnostics and source-map reporting gaps found with winapp DevTools (# |
| 2026-10-29 | 8f17bac9 | E2 | 240 | 27 | 0 | 11 | W1 | Avoid per-child control reads in keyed mapping around retained exits (#1445 |
| 2026-10-29 | 90c99595 | E2 | 153 | 22 | 0 | 7 | W1 | Fix keyed child mapping while exit transitions are retained (#1429) |
| 2026-11-02 | b0e0fe30 | E1 | 18 | 0 | 24 | 9 | O1 | Fix UsePersisted null restoration after remount (#1443) |
| 2026-11-02 | fb64db02 | E4 | 77 | 0 | 63 | 18 | O1 | Fix resource cache dependency hash collisions (#1431) |
| 2026-11-03 | 96ccfc4b | E1+E4 | 35 | 12 | 18 | 20 | O1+S+W1 | Refactor: Consolidate hook dependency equality in RenderContext (#1451) |
| 2026-11-04 | 59315be5 | E5 | 15 | 0 | 5 | 7 | O1 | Fix UseDataSource initial page-size consistency (#1453) |
| 2026-11-05 | fa643977 | E3 | 755 | 0 | 0 | 19 | - | Sync the Yoga layout engine to upstream facebook/yoga@41118bd (#1466) |
| 2026-11-06 | aa7fd628 | E5 | 34 | 0 | 9 | 8 | O1 | Keep UseDataSource page size consistent across renders (#1458) |
| 2026-11-09 | f45d2edb | E4 | 2 | 0 | 2 | 10 | O1 | Fix shared page-cache isolation across different page sizes (#1471) |
| 2026-11-10 | 0cdc78ad | E1+E2 | 54 | 265 | 629 | 28 | O1+W1+W4 | Emit Reactor perf spans recordable by non-elevated private ETW sessions (Mi |
| 2026-11-12 | 587dd6ae | E2 | 7 | 561 | 25 | 26 | O1+W1+W2 | Fix target callbacks for flyout decorators (#1472) |
| 2026-11-18 | fbeb1d76 | E1 | 17 | 0 | 0 | 11 | - | Defer hook callback marshaling allocations to off-thread calls (#1580) |
| 2026-11-19 | 1cf3840e | E1 | 5 | 0 | 0 | 7 | - | Avoid inline UsePersisted marshaling allocations (#1604) |
| 2026-11-19 | 7258869c | E2 | 2 | 73 | 124 | 13 | O1+W1+W2 | Move facade transforms to the visual before handing it out (#1627) |
| 2026-11-19 | ad0ab15f | E1+E2 | 41 | 232 | 214 | 25 | O1+W1+W2+W3 | Add reconciler-aware perf metrics: memo hits, no-op renders, setState fan-o |
| 2026-11-19 | cd8d52f0 | E2 | 26 | 0 | 0 | 9 | - | Avoid fallback key strings in mixed-key panel reorders (#1611) |

## C co-touched areas in commits that touch E (whole history, 89 commits; CHANGELOG.md touched in 44)
| area | commits also touching it |
|---|---|
| T | 82 |
| X | 68 |
| D | 64 |
| O1 | 61 |
| W1 | 52 |
| W4 | 41 |
| W3 | 33 |
| S | 28 |
| W2 | 18 |
| W5 | 2 |

| engine area | commits | no other src code (no W*, O1, S) | % |
|---|---|---|---|
| E1 | 36 | 4 | 11% |
| E2 | 27 | 3 | 11% |
| E3 | 6 | 3 | 50% |
| E4 | 26 | 2 | 8% |
| E5 | 17 | 1 | 6% |
| E* | 89 | 12 | 13% |

## L landing, window 2026-08-22..11-22
| metric | value |
|---|---|
| first-parent commits | 334 |
| merge commits on first-parent | 0 |
| subject ends with (#NNN) (GitHub squash) | 334 |
| "Merge pull request #" subjects | 0 |
| "Merged PR NNN" (Azure DevOps) subjects | 0 |
| median lines changed per commit (excl. bulk) | 210.5 |
| p75 / p90 lines | 835 / 2081 |
| mean lines | 778 |
| median files per commit | 6.0 |
| commits touching src/Reactor | 121 |
| median src/Reactor lines in those | 102 |
| calendar days | 91 |
| days with >=1 commit | 55 |
| commits per calendar day | 3.67 |
| max commits in a day | 22 |
| distinct authors | 16 |

## L landing, whole history
| metric | value |
|---|---|
| first-parent commits | 1244 |
| merge commits on first-parent | 225 |
| subject ends with (#NNN) (GitHub squash) | 865 |
| "Merge pull request #" subjects | 168 |
| "Merged PR NNN" (Azure DevOps) subjects | 206 |
| median lines changed per commit (excl. bulk) | 302.0 |
| p75 / p90 lines | 1028 / 2502 |
| mean lines | 1121 |
| median files per commit | 5.0 |
| commits touching src/Reactor | 392 |
| median src/Reactor lines in those | 122.5 |
| calendar days | 250 |
| days with >=1 commit | 178 |
| commits per calendar day | 4.98 |
| max commits in a day | 22 |
| distinct authors | 31 |

PR numbers in window subjects: min #1085, max #1653, 334 landed of 569 numbers in range (issues and PRs share the counter)

## O1 breakdown (window)
| sub-area | commits | lines |
|---|---|---|
| Core/Diagnostics | 16 | 4287 |
| Core | 39 | 3098 |
| Core/Navigation | 6 | 1326 |
| Diagnostics | 10 | 994 |
| Animation | 5 | 351 |
| build | 2 | 227 |
| Controls | 6 | 146 |
| Reactor.csproj | 8 | 87 |
| ThirdPartyNoticeText.txt | 2 | 61 |
| Core/Internal | 3 | 37 |
| Core/Polyfills | 1 | 9 |
| Input | 1 | 2 |
| README.md | 1 | 1 |

## T breakdown (window)
| test project | commits | lines |
|---|---|---|
| tests/Reactor.AppTests.Host | 114 | 58968 |
| tests/Reactor.Tests | 133 | 52165 |
| tests/Reactor.AppTests | 13 | 6949 |
| tests/Reactor.PackagedTests | 6 | 6001 |
| tests/Reactor.DocPipeline.Tests | 16 | 5472 |
| tests/ToolkitCompatibility | 4 | 4922 |
| tests/Reactor.SourceMap.Tests | 8 | 4373 |
| tests/Reactor.SelfTests | 7 | 1539 |
| tests/_shared | 3 | 1440 |
| tests/build_props | 6 | 1338 |
| tests/coverage | 2 | 1125 |
| tests/Reactor.IntegrationTests | 5 | 1066 |
| tests/perf_bench | 5 | 980 |
| tests/vs_reactor | 5 | 698 |
| tests/external_proof | 3 | 258 |

## O1 top files (window)
| file | commits | lines |
|---|---|---|
| Reactor.csproj | 8 | 87 |
| Core/ElementPool.cs | 7 | 101 |
| Diagnostics/ReactorSourceMap.cs | 6 | 298 |
| Core/Component.cs | 6 | 59 |
| Core/ElementFactory.cs | 6 | 210 |
| Animation/AnimationHelper.cs | 5 | 342 |
| Core/InfiniteResource.cs | 4 | 45 |
| Core/Diagnostics/ReactorEventSource.cs | 4 | 92 |
| Core/Diagnostics/ReactorDiagnostics.cs | 4 | 139 |
| Core/Theme.cs | 3 | 177 |
| Core/Navigation/ConnectedNavigationOperation.cs | 3 | 369 |
| Core/Navigation/TransitionEngine.cs | 3 | 768 |
| Core/Diagnostics/ReactorPerfTrace.cs | 3 | 774 |
| Core/Diagnostics/ReactorDiagnostics.Source.cs | 3 | 108 |
| Core/Diagnostics/ReactorSourcePublisher.cs | 3 | 460 |
| Core/Diagnostics/ReactorDiagnostics.Components.cs | 3 | 461 |
| Core/Diagnostics/ComponentRenderTrace.cs | 3 | 461 |
| Core/QueryCache.cs | 3 | 19 |
| Core/SourceLocation.cs | 3 | 88 |
| Core/Diagnostics/ReactorDiagnostics.Hosts.cs | 3 | 398 |

## B baseline delta 28a87225..14172dce: commits
```
14172dce 2026-11-22 Spec 066 layer V: transforms validation matrix and behaviour ledger (#1653)
73cf6e6c 2026-11-22 Add bounded OS text-scale checks for Toolkit SettingsCard (#1648)
```
name-status:
```
M	.github/workflows/ci.yml
A	.github/workflows/transforms-matrix.yml
M	CHANGELOG.md
M	Reactor.slnx
M	TESTING.md
M	docs/specs/066-transforms-on-composition-visual.md
M	tests/Reactor.AppTests.Host/Reactor.AppTests.Host.csproj
A	tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.expected.json
A	tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.json
A	tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.known.json
A	tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixFixture.cs
A	tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixProbes.cs
A	tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixRecorder.cs
M	tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs
A	tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/CompositionModel.cs
A	tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/MatrixCase.cs
A	tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/TransformsLedger.cs
M	tests/Reactor.PackagedTests.Host/Reactor.PackagedTests.Host.csproj
M	tests/Reactor.Tests/Reactor.Tests.csproj
A	tests/Reactor.Tests/TransformsMatrix/TransformsMatrixTests.cs
A	tests/ToolkitCompatibility.HeadlessTests/TextScalingMeasurementsTests.cs
A	tests/ToolkitCompatibility.HeadlessTests/ToolkitCompatibility.HeadlessTests.csproj
M	tests/ToolkitCompatibility/LocalEvidenceChecks.cs
M	tests/ToolkitCompatibility/Program.cs
M	tests/ToolkitCompatibility/README.md
A	tests/ToolkitCompatibility/TextScalingChecks.cs
A	tests/ToolkitCompatibility/TextScalingMeasurements.cs
```
| area | files | added | deleted |
|---|---|---|---|
| T | 21 | 6939 | 4 |
| D | 1 | 49 | 1 |
| X | 5 | 252 | 0 |

| area | file | added | deleted |
|---|---|---|---|
| D | docs/specs/066-transforms-on-composition-visual.md | 49 | 1 |
| T | tests/Reactor.AppTests.Host/Reactor.AppTests.Host.csproj | 6 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.expected.json | 3 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.json | 3349 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Baselines/transforms-ledger.known.json | 10 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixFixture.cs | 698 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixProbes.cs | 271 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/Fixtures/TransformsMatrixRecorder.cs | 875 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/SelfTestFixtureRegistry.cs | 3 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/CompositionModel.cs | 225 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/MatrixCase.cs | 172 | 0 |
| T | tests/Reactor.AppTests.Host/SelfTest/TransformsMatrix/TransformsLedger.cs | 361 | 0 |
| T | tests/Reactor.PackagedTests.Host/Reactor.PackagedTests.Host.csproj | 3 | 0 |
| T | tests/Reactor.Tests/Reactor.Tests.csproj | 3 | 0 |
| T | tests/Reactor.Tests/TransformsMatrix/TransformsMatrixTests.cs | 461 | 0 |
| T | tests/ToolkitCompatibility.HeadlessTests/TextScalingMeasurementsTests.cs | 135 | 0 |
| T | tests/ToolkitCompatibility.HeadlessTests/ToolkitCompatibility.HeadlessTests.csproj | 14 | 0 |
| T | tests/ToolkitCompatibility/LocalEvidenceChecks.cs | 14 | 3 |
| T | tests/ToolkitCompatibility/Program.cs | 1 | 1 |
| T | tests/ToolkitCompatibility/README.md | 63 | 0 |
| T | tests/ToolkitCompatibility/TextScalingChecks.cs | 201 | 0 |
| T | tests/ToolkitCompatibility/TextScalingMeasurements.cs | 71 | 0 |
| X | .github/workflows/ci.yml | 32 | 0 |
| X | .github/workflows/transforms-matrix.yml | 92 | 0 |
| X | CHANGELOG.md | 5 | 0 |
| X | Reactor.slnx | 1 | 0 |
| X | TESTING.md | 122 | 0 |

## P weekly pull simulation: git diff -M --numstat <last mainline commit before Monday> <last before next Monday> restricted to E paths
| ISO week | from | to | commits touching E | E files changed | added | deleted | files (lines) |
|---|---|---|---|---|---|---|---|
| 2026-W35 | bebd6f5e | fceaf526 | 2 | 3 | 37 | 20 | Core/RenderContext.cs (45); Hooks/UseFocusTrap.cs (6); Hooks/UseMemoCells.cs (6) |
| 2026-W36 | fceaf526 | dcb9ecb3 | 2 | 2 | 20 | 6 | Core/ChildReconciler.cs (15); Hooks/Pending.cs (11) |
| 2026-W37 | dcb9ecb3 | d9ee0d9b | 0 | 0 | 0 | 0 |  |
| 2026-W38 | d9ee0d9b | 9aa52909 | 0 | 0 | 0 | 0 |  |
| 2026-W39 | 9aa52909 | 85802e65 | 0 | 0 | 0 | 0 |  |
| 2026-W40 | 85802e65 | 28ace24d | 2 | 7 | 2119 | 110 | Controls/Validation/UseValidationContext.cs (57); Controls/Validation/ValidateExtensions.cs (129); Controls/Validation/ValidationContext.cs (1168); Controls/Validation/ValidationReconciler.cs (220); Controls/Validation/ValidationRenderScope.cs (359); Controls/Validation/ValidationRule.cs (149); Core/ChildReconciler.cs (147) |
| 2026-W41 | 28ace24d | aed6870a | 2 | 6 | 51 | 14 | Controls/Validation/ValidationContext.cs (2); Core/RenderContext.cs (55); Data/DataPageCache.cs (2); Data/Providers/ListDataSource.cs (2); Hooks/PendingScope.cs (2); Hooks/UseMutation.cs (2) |
| 2026-W42 | aed6870a | e1681952 | 1 | 1 | 29 | 1 | Core/RenderContext.cs (30) |
| 2026-W43 | e1681952 | 6a4b8bd9 | 7 | 2 | 377 | 117 | Core/ChildReconciler.cs (43); Core/RenderContext.cs (451) |
| 2026-W44 | 6a4b8bd9 | 74791562 | 5 | 2 | 732 | 118 | Core/ChildReconciler.cs (539); Core/Internal/KeyedListDiff.cs (311) |
| 2026-W45 | 74791562 | af4ecc44 | 6 | 12 | 789 | 133 | Core/RenderContext.cs (33); Data/DataSourceResourceExtensions.cs (41); Hooks/UseInfiniteResource.cs (35); Hooks/UseMemoCells.cs (16); Hooks/UseResource.cs (42); Yoga/AlgorithmUtils.cs (91); Yoga/LayoutResults.cs (30); Yoga/YogaAlgorithm.cs (558); Yoga/YogaConfig.cs (6); Yoga/YogaEnums.cs (2); Yoga/YogaNode.cs (48); Yoga/YogaStyle.cs (20) |
| 2026-W46 | af4ecc44 | 68892293 | 3 | 4 | 58 | 5 | Core/ChildReconciler.cs (7); Core/Internal/KeyedListDiff.cs (12); Core/RenderContext.cs (42); Hooks/UseInfiniteResource.cs (2) |
| 2026-W47 | 68892293 | 14172dce | 5 | 3 | 66 | 25 | Core/ChildReconciler.cs (28); Core/Internal/KeyedListDiff.cs (7); Core/RenderContext.cs (56) |

weeks=13 weeks with E change=10 median lines/week=63 mean=371 max=2229 total files-weeks=42

## WR WinUI-reference lines per E file at origin/main (regex in winui_regex.txt)
| area | file | lines | lines matching WinUI regex |
|---|---|---|---|
| E5 | Controls/Validation/ErrorStyling.cs | 158 | 3 |
| E5 | Controls/Validation/FormField.cs | 164 | 7 |
| E5 | Controls/Validation/ImmediateExtensions.cs | 28 | 2 |
| E5 | Controls/Validation/UseValidationContext.cs | 120 | 2 |
| E5 | Controls/Validation/ValidateExtensions.cs | 253 | 3 |
| E5 | Controls/Validation/ValidationContext.cs | 1565 | 2 |
| E5 | Controls/Validation/ValidationMessage.cs | 24 | 1 |
| E5 | Controls/Validation/ValidationReconciler.cs | 262 | 3 |
| E5 | Controls/Validation/ValidationRenderScope.cs | 359 | 2 |
| E5 | Controls/Validation/ValidationRule.cs | 221 | 3 |
| E5 | Controls/Validation/ValidationVisualizer.cs | 179 | 3 |
| E5 | Controls/Validation/Validators/BuiltInValidators.cs | 238 | 1 |
| E5 | Controls/Validation/Validators/IValidator.cs | 25 | 1 |
| E2 | Core/ChildReconciler.cs | 1454 | 26 |
| E2 | Core/Internal/KeyedListDiff.cs | 938 | 18 |
| E1 | Core/RenderContext.cs | 3153 | 77 |
| E5 | Data/DataPage.cs | 18 | 1 |
| E5 | Data/DataPageCache.cs | 345 | 2 |
| E5 | Data/DataRequest.cs | 25 | 1 |
| E5 | Data/DataSourceResourceExtensions.cs | 97 | 4 |
| E5 | Data/FieldDescriptor.cs | 106 | 3 |
| E5 | Data/FilterDescriptor.cs | 30 | 1 |
| E5 | Data/GridAttributes.cs | 33 | 1 |
| E5 | Data/IDataSource.cs | 60 | 1 |
| E5 | Data/Providers/ListDataSource.cs | 224 | 1 |
| E5 | Data/Providers/ObservableListDataSource.cs | 104 | 1 |
| E5 | Data/RowKey.cs | 13 | 1 |
| E5 | Data/SortDescriptor.cs | 15 | 1 |
| E4 | Hooks/Pending.cs | 89 | 7 |
| E4 | Hooks/PendingScope.cs | 81 | 1 |
| E4 | Hooks/UseAnnounce.cs | 173 | 21 |
| E4 | Hooks/UseDevtools.cs | 29 | 2 |
| E4 | Hooks/UseElementFocus.cs | 62 | 10 |
| E4 | Hooks/UseElementRef.cs | 57 | 6 |
| E4 | Hooks/UseFocus.cs | 254 | 8 |
| E4 | Hooks/UseFocusTrap.cs | 173 | 14 |
| E4 | Hooks/UseInfiniteResource.cs | 460 | 3 |
| E4 | Hooks/UseMemoCells.cs | 524 | 2 |
| E4 | Hooks/UseMutation.cs | 346 | 4 |
| E4 | Hooks/UseResource.cs | 520 | 9 |
| E3 | Yoga/AlgorithmUtils.cs | 551 | 2 |
| E3 | Yoga/FlexDirectionHelper.cs | 81 | 2 |
| E3 | Yoga/FlexEnums.cs | 63 | 1 |
| E3 | Yoga/LayoutResults.cs | 218 | 2 |
| E3 | Yoga/YogaAlgorithm.cs | 2639 | 2 |
| E3 | Yoga/YogaConfig.cs | 131 | 2 |
| E3 | Yoga/YogaEnums.cs | 128 | 3 |
| E3 | Yoga/YogaNode.cs | 697 | 2 |
| E3 | Yoga/YogaStyle.cs | 422 | 2 |
| E3 | Yoga/YogaValue.cs | 116 | 2 |

E files: 50, lines 18025, files with 0 matches: 0 (0 lines)