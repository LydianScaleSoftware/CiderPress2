# Prompt Guidance — Blueprint Critique Prompts

**Purpose:** Each prompt below is designed to be given to a Claude Sonnet agent
to produce a companion critique file for the corresponding blueprint. The
critique files should be saved alongside the blueprints in the
`cp2_avalonia/MVVM_Project/` directory.

These prompts can be run in parallel — each is self-contained.

---

## ~~Prompt 1: Pre-Iteration-Notes Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a planning document that will be read by an implementing agent
before every iteration of work.

Read these files:
1. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (the document to critique)
2. cp2_avalonia/MVVM_Project/MVVM_Notes.md (the master plan, for context)

Then create a file called cp2_avalonia/MVVM_Project/Pre-Iteration-Notes_Critique.md
containing your critique.

Your critique should identify:
- Places where an implementing agent would lack enough information to proceed
  without guessing
- Ambiguities that could lead to inconsistent decisions across iterations
- Rules or conventions stated here that conflict with what MVVM_Notes.md says
- Missing conventions that an implementing agent would need (naming, file
  placement, error handling patterns, etc.)
- Anything stated too vaguely to be actionable (e.g., "use best practices"
  without specifics)
- Whether the validation checklist is complete enough to catch regressions

Do NOT suggest adding content that belongs in the individual blueprints rather
than this cross-cutting document. Focus only on what a priming/conventions
document should contain.

Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Section or line reference
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition
```

---

## ~~Prompt 2: Iteration 0 Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_0_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 0 and §4)

Also read these source files to verify the blueprint's claims against reality:
4. cp2_avalonia/cp2_avalonia.csproj (current NuGet references)
5. cp2_avalonia/App.axaml.cs (current startup code)
6. cp2_avalonia/MainWindow.axaml.cs (inner classes to extract)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_0_Critique.md
containing your critique.

Your critique should identify:
- Steps that are too vague for an agent to execute without making assumptions
- Incorrect assumptions about the current codebase (wrong class names, wrong
  file locations, wrong API signatures)
- Missing steps that would leave the build broken or tests failing
- Ordering issues (steps that depend on something not yet done)
- Steps that contradict Pre-Iteration-Notes.md or MVVM_Notes.md
- Code snippets that wouldn't compile against the actual project
- Missing validation steps

Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or section
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition


Your goal is to surface all issues that would materially affect an implementing agent. The document may be revised and re‑critiqued based on your findings.
```
---

## ~~Prompt 3: Iteration 1A Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_1A_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 1A and §7.13)

Also read these source files to verify the blueprint's claims against reality:
4. cp2_avalonia/MainWindow.axaml.cs (properties to move — read the full file)
5. cp2_avalonia/MainController_Panels.cs (canExecute state properties)
6. cp2_avalonia/MainController.cs (first 100 lines for field declarations)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_1A_Critique.md
containing your critique.

Your critique should identify:
- Properties listed in the blueprint that don't actually exist in MainWindow.axaml.cs
- Properties that exist in MainWindow.axaml.cs but are missing from the blueprint's lists
- Incorrect property types, names, or groupings
- Steps that are too vague for an agent to execute without making assumptions
- Missing steps that would leave the build broken
- Any properties that should NOT be moved (view-only concerns that belong on the Window)
- Whether the "do NOT switch DataContext" instruction is clear enough

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.

Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or property name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.   The document may be revised and re‑critiqued based on your findings.  

```

---

## ~~Prompt 4: Iteration 1B Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_1B_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 1B and §7.13)
4. cp2_avalonia/MVVM_Project/Iteration_1A_Blueprint.md (the preceding phase)

Also read these source files:
5. cp2_avalonia/MainWindow.axaml.cs (full file — understand current DataContext wiring)
6. cp2_avalonia/MainWindow.axaml (AXAML bindings — check for x:Name references,
   event handlers, and binding patterns that would break)
7. cp2_avalonia/MainController.cs (all mMainWin.PropertyName accesses)
8. cp2_avalonia/MainController_Panels.cs (all mMainWin.PropertyName accesses)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_1B_Critique.md
containing your critique.

Your critique should identify:
- Controller property accesses (mMainWin.X) that the blueprint fails to redirect
- AXAML bindings that would break when DataContext changes from Window to ViewModel
- Event handlers in AXAML that reference code-behind methods — are they all accounted for?
- Properties the blueprint says to remove from MainWindow that are still needed
  by code-behind event handlers
- Missing steps in the controller redirect process
- Whether the validation steps would actually catch binding failures


Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.


Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or specific code reference
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.

```

---

## ~~Prompt 5: Iteration 2 Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_2_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 2 and §7.13)
4. cp2_avalonia/MVVM_Project/Iteration_1B_Blueprint.md (the preceding phase)

Also read these source files:
5. cp2_avalonia/MainWindow.axaml.cs (all 51 ICommand properties — read the full file
   to find every RelayCommand instantiation and its canExecute predicate)
6. cp2_avalonia/MainWindow.axaml (verify command bindings in AXAML)
7. cp2_avalonia/App.axaml.cs (native menu handlers)
8. cp2_avalonia/Common/RelayCommand.cs (understand current command infrastructure)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_2_Critique.md
containing your critique.

Your critique should identify:
- Commands listed in the blueprint that don't exist, or commands that exist but
  are missing from the blueprint
- Incorrect canExecute conditions (compare blueprint's WhenAnyValue expressions
  against actual RelayCommand predicates in the source)
- Commands that are async in the source but shown as sync in the blueprint
  (or vice versa)
- Whether the command count (51) is accurate
- Missing steps for keyboard shortcuts or input bindings tied to commands
- Whether ReactiveCommand.Execute().Subscribe() pattern for native menu is correct
- Any commands with parameters (not Unit) that the blueprint treats as parameterless

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.


Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or command name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns. The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 6: Iteration 3A Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_3A_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §4, §7.14, §7.15,
   §7.17, §7.19)

Also read these source files to verify interface designs against actual usage:
4. cp2_avalonia/MainController.cs (all dialog creation, file picker, clipboard,
   and settings access patterns)
5. cp2_avalonia/MainController_Panels.cs (same scan)
6. cp2_avalonia/App.axaml.cs (current startup structure)
7. cp2_avalonia/AppSettings.cs (settings architecture)
8. cp2_avalonia/MainWindow.axaml.cs (how controller is instantiated)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_3A_Critique.md
containing your critique.

Your critique should identify:
- Service interface methods that don't match how the controller actually uses
  those capabilities (e.g., IClipboardService methods vs actual clipboard code)
- Missing service methods needed by controller code that aren't in any interface
- DI lifetime choices (singleton vs transient) that could cause bugs
- Whether the DialogService implementation would actually work (type registration,
  ShowDialog return value, DataContext assignment)
- Code snippets that wouldn't compile (wrong namespaces, missing usings,
  API mismatches with Avalonia's actual StorageProvider/Clipboard APIs)
- Circular dependency risks in the DI registration
- Whether IDialogHost is sufficient or if more view capabilities are needed
- Whether the SettingsService wrapper adds value or creates confusion

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.


Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or interface/method name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 7: Iteration 3B Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_3B_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 3B and
   all of §7)
4. cp2_avalonia/MVVM_Project/Iteration_3A_Blueprint.md (the preceding phase —
   service interfaces available)

Also read these source files thoroughly:
5. cp2_avalonia/MainController.cs (full file — this is being dissolved)
6. cp2_avalonia/MainController_Panels.cs (full file — this is being dissolved)
7. cp2_avalonia/MainWindow.axaml.cs (code-behind that calls controller methods)
8. cp2_avalonia/MainWindow.axaml (event handlers wired to code-behind)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_3B_Critique.md
containing your critique.

Your critique should identify:
- Controller methods or fields missing from the blueprint's migration inventory
- Methods whose migration destination (VM vs service vs child VM) is wrong or
  unclear
- The incremental migration order — would it actually keep the app compiling
  at each step?
- Direct control access patterns (mMainWin.someControl.SomeMethod) that the
  blueprint doesn't account for
- Thread marshaling (Dispatcher.UIThread) patterns in the controller that
  need special handling in the ViewModel
- Event subscriptions in the controller that need equivalent reactive wiring
- Whether the IViewActions interface is sufficient for all view-specific operations
- Whether WorkspaceService can actually be implemented with the proposed interface
  given how WorkTree lifecycle actually works in the controller

This is the most complex phase. Be thorough — an implementing agent following
this blueprint with gaps will get stuck.


Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.



Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number, method name, or line reference
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 8: Iteration 4A Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_4A_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §3.3, §6 Phase 4A,
   §7.14)

Also read these dialog source files (the ones being converted):
4. cp2_avalonia/EditSector.axaml.cs (full file)
5. cp2_avalonia/Tools/FileViewer.axaml.cs (full file)
6. cp2_avalonia/EditAttributes.axaml.cs (full file)
7. cp2_avalonia/CreateDiskImage.axaml.cs (full file)
8. cp2_avalonia/SaveAsDisk.axaml.cs (full file)

Also read the corresponding AXAML files for at least EditSector and FileViewer
to understand the binding surface.

Then create a file called cp2_avalonia/MVVM_Project/Iteration_4A_Critique.md
containing your critique.

Your critique should identify:
- Constructor parameters listed in the blueprint that don't match what the
  dialog actually needs (compare against actual constructors)
- Properties or logic in the dialog code-behind that the blueprint fails to
  mention moving
- Inner classes, enums, or delegates in the dialog files that need migration
- Dialog result patterns — how does the caller get results back? Is this clear?
- Code-behind that CANNOT be moved to a ViewModel (platform-specific rendering,
  control manipulation) — is the "retained in code-behind" list complete?
- Whether the DialogService.ShowDialogAsync pattern works for dialogs that
  need constructor parameters beyond just a ViewModel
- File Viewer multi-instance lifecycle — is it adequately specified?

Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or dialog name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.



Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 9: Iteration 4B Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_4B_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §3.3, §7.14)
4. cp2_avalonia/MVVM_Project/Iteration_4A_Blueprint.md (preceding phase — pattern
   established there)

Also read these dialog source files:
5. cp2_avalonia/EditAppSettings.axaml.cs
6. cp2_avalonia/Common/WorkProgress.axaml.cs
7. cp2_avalonia/EditConvertOpts.axaml.cs
8. cp2_avalonia/FindFile.axaml.cs
9. cp2_avalonia/CreateDirectory.axaml.cs
10. cp2_avalonia/Actions/OverwriteQueryDialog.axaml.cs
11. cp2_avalonia/ReplacePartition.axaml.cs

Then create a file called cp2_avalonia/MVVM_Project/Iteration_4B_Critique.md
containing your critique.

Your critique should identify:
- Constructor parameters in the blueprint that don't match actual dialog constructors
- WorkProgress is used by many callers — does the ViewModel interface work for
  all of them? Check how WorkProgress is instantiated across MainController.cs
- OverwriteQueryDialog is shown DURING WorkProgress operations — how does this
  nesting work with IDialogService?
- FindFile is modeless and communicates results back to MainViewModel — is the
  callback/interaction mechanism specified?
- EditAppSettings applies settings changes that affect the entire app (theme,
  etc.) — is the notification mechanism clear?
- Whether the "retire RelayCommand" step is premature or correctly placed
- Missing dialogs that exist in the codebase but aren't listed

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.


Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or dialog name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 10: Iteration 5 Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_5_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 5, §7.22,
   §7.11)

Also read these source files to understand the properties/methods being extracted:
4. cp2_avalonia/MainWindow.axaml.cs (understand current property groupings)
5. cp2_avalonia/MainWindow.axaml (understand current AXAML binding paths that
   will change from {Binding Prop} to {Binding ChildVM.Prop})
6. cp2_avalonia/MainController_Panels.cs (methods that populate trees/lists)
7. cp2_avalonia/ArchiveTreeItem.cs (tree item data model)
8. cp2_avalonia/DirectoryTreeItem.cs (tree item data model)
9. cp2_avalonia/FileListItem.cs (file list data model)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_5_Critique.md
containing your critique.

Your critique should identify:
- Properties assigned to the wrong child ViewModel
- Properties that need to stay on MainViewModel because they're used by commands
  or cross-panel logic, but the blueprint moves them to a child VM
- Cross-panel communication patterns that WhenAnyValue subscriptions can't handle
  (e.g., methods that need to coordinate multiple child VMs atomically)
- AXAML binding path changes that the blueprint doesn't enumerate — would an
  agent know exactly which bindings to update?
- Whether TreeView SelectedItem two-way binding actually works in Avalonia
  (it's historically problematic)
- Missing child ViewModel — should there be others?
- Whether the size estimates are realistic given the method inventory from Phase 3B
- Disposal/lifecycle of child VMs and their subscriptions

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.


Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Step number or ViewModel name
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns. The document may be revised and re‑critiqued based on your findings.
```

---

## ~~Prompt 11: Iteration 6 Blueprint Critique~~

```
You are reviewing an MVVM refactoring project for a .NET/Avalonia desktop
application called CiderPress2 (an Apple II disk/archive utility). Your task
is to critique a blueprint that an implementing agent will follow step by step.

Read these files:
1. cp2_avalonia/MVVM_Project/Iteration_6_Blueprint.md (the blueprint to critique)
2. cp2_avalonia/MVVM_Project/Pre-Iteration-Notes.md (conventions/context)
3. cp2_avalonia/MVVM_Project/MVVM_Notes.md (master plan — read §6 Phase 6, §7.10,
   §7.11, §4.6)

Then create a file called cp2_avalonia/MVVM_Project/Iteration_6_Critique.md
containing your critique.

Your critique should identify:
- Whether the "cleanup" workstream (A) has concrete enough criteria — would an
  agent know when it's done?
- Whether the completion criteria checklist is complete and measurable
- Whether the ViewerService implementation handles edge cases (viewer outliving
  source, thread safety, UI thread requirements)
- Whether the unit test infrastructure section gives enough guidance to actually
  write tests (mock setup, what to assert, test naming)
- Whether IActivatableViewModel / DisposeWith is the right lifecycle pattern
  and if the blueprint explains it enough for an implementing agent
- Items that should have been done in earlier phases but were deferred here
  (are they correctly placed or should they move earlier?)
- Whether "optional" workstreams have clear go/no-go criteria

Assume that any issues fixed in previous iterations are now correct. Do not re‑verify or re‑evaluate earlier findings; only critique what is currently written in the blueprint.

Format your critique as a numbered list of findings, each with:
- **Finding:** What the issue is
- **Location:** Workstream letter or step number
- **Impact:** What could go wrong if unaddressed
- **Suggestion:** Concrete fix or addition

Your goal is to surface all issues that would materially affect an implementing agent (correctness, buildability, or unambiguous execution). Do not provide deep architectural analysis or extended reasoning. Only report concrete, actionable findings based on the blueprint and the referenced files. Ignore stylistic or cosmetic concerns.  The document may be revised and re‑critiqued based on your findings.
```

---

## Prompt 12: Apply Critique Fixes to a Document

```
You are an implementing agent for an MVVM refactoring project for a .NET/Avalonia
desktop application called CiderPress2 (an Apple II disk/archive utility).

A separate critique agent has reviewed a planning document and produced a numbered
list of findings in a companion critique file. Your job is to review each finding,
assess its validity, and apply the accepted fixes to the source document.

**Context files to read first:**
1. cp2_avalonia/MVVM_Project/MVVM_Notes.md — the master MVVM plan (~1,200 lines).
   This is the authoritative source for architecture decisions, service interfaces,
   phase scopes, and conventions.
2. The document being critiqued (the user will tell you which one).
3. The critique file (the user will tell you which one, and which finding numbers
   are new — earlier findings have already been applied).

**Workflow:**

This is an **iterative** process. The critique agent will make multiple passes
over the document. Each pass produces new findings (numbered sequentially from
where the previous pass left off). You apply the fixes, the user sends the
updated document back to the critique agent, and the cycle repeats until the
critique agent declares the document stable. Expect early rounds to produce
10–15 structural fixes and later rounds to narrow to 2–4 terminology/edge-case
items. The user will tell you which finding numbers are new in each round.

For each round:

1. Read the critique file to understand all new findings.
2. Read the affected sections of the source document to get exact text for edits.
3. Cross-reference findings against MVVM_Notes.md to verify factual claims
   (e.g., parameter order, member names, phase scopes, service lifetimes).
4. Present a summary table of all findings with your assessment and proposed
   disposition for each:
   - **Accept:** Finding is correct; apply the fix.
   - **Accept with modification:** Finding is directionally correct but the
     suggested fix needs adjustment.
   - **Reject:** Finding is incorrect or the current text is already adequate.
5. Wait for user confirmation, then apply all accepted fixes.
6. Use multi_replace_string_in_file to batch independent edits efficiently.
7. After applying, verify the result (e.g., check section numbering, grep for
   consistency of terminology across both the source document and MVVM_Notes.md).

**Rules:**
- You must NOT run any git commands. The user handles all version control.
- Always read the exact text before editing — do not guess at file contents.
- Include 3–5 lines of unchanged context before and after each edit target.
- When a fix requires changing terminology, check BOTH the source document AND
  MVVM_Notes.md for consistency (e.g., `SettingChanged` vs `SettingsChanged`).
- If a finding identifies a conflict between the source document and
  MVVM_Notes.md, MVVM_Notes.md is authoritative unless the user says otherwise.
- Do not create new markdown files to document changes unless requested.
- Do not add content beyond what the finding requires — no over-engineering.

**Session memory (context preservation):**
This work is iterative and conversations may be compacted mid-session. To
preserve continuity across compaction events:
- After each round of fixes, write or update a session memory file
  (`/memories/session/critique-cycle-state.md`) recording: which document and
  critique file are being processed, which finding numbers have been applied,
  a brief summary of each round's themes, and any patterns or decisions to
  carry forward.
- At the start of each new round, read the session memory file to recover
  state before reading the critique file.
- Keep the session file concise — bullet points, not prose.
```
===============================================

# Opus Orchestrator Prompt — Generate Developer Manuals in Parallel

You are an **orchestrator agent** coordinating multiple sub‑agents.

Your goal is to generate **Developer Manuals** for all blueprint iterations in the workspace **in parallel**, using sub‑agents so that no single agent is overloaded with context.

The user is **new to MVVM and new to ReactiveUI**.  
All explanations in the generated manuals must reflect that.

---

## 1. Discover all blueprint files

1. Enumerate all files in the workspace whose names match:

   `Iteration_*_Blueprint.md`

2. For each matching file:

   - Extract the iteration identifier from the filename.  
     - Example:  
       - `Iteration_3A_Blueprint.md` → iteration identifier = `3A`  
       - `Iteration_7_Blueprint.md` → iteration identifier = `7`
   - Bind this identifier to the placeholder `XX` for that iteration.

If no blueprint files are found, stop and report that to the user.

---

## 2. For each blueprint, spawn a sub‑agent

For **each** `Iteration_XX_Blueprint.md` file you discovered, spawn a **sub‑agent** with the following prompt.

> ### Sub‑Agent Prompt — Generate Developer Manual for Iteration_XX
>
> You are generating a **Developer Manual** from a blueprint.  
> The Developer Manual is a *teaching‑oriented*, human‑readable expansion of the blueprint.
>
> The iteration identifier for this run is: `XX`.  
> Use this identifier for all filenames and references.
>
> Automatically load the following files from the workspace:
>
> 1. **Iteration_XX_Blueprint.md**  
> 2. **MVVM_Notes.md** (the architectural source of truth)
>
> The user is **new to MVVM and new to ReactiveUI**.  
> You must explain concepts, patterns, and terminology accordingly.
>
> ---
>
> ## Your tasks
>
> ### 1. Treat MVVM_Notes.md as the authoritative design document
> - The blueprint is a detailed expansion of items in MVVM_Notes.md.  
> - The Developer Manual is a teaching expansion of the blueprint.  
> - All three documents must remain consistent.
>
> ### 2. If you detect any discrepancy between MVVM_Notes.md and the blueprint
> - **STOP.**  
> - Do **not** assume which document is correct.  
> - Do **not** “fix” the blueprint.  
> - Instead, output a short message asking the user for clarification.
>
> ### 3. If no discrepancies exist, generate the Developer Manual
> Produce a complete Markdown document named:
>
> **Iteration_XX_Developer_Manual.md**
>
> For each major section of the blueprint, use this structure:
>
> ---
>
> ## Section Title (copied from blueprint)
>
> ### What we are going to accomplish
> Explain:
> - the goals of the section  
> - the reasoning behind the changes  
> - the architectural/MVVM context  
> - the ReactiveUI concepts involved  
> - why this step appears in this iteration  
>
> Assume the user is new to MVVM and ReactiveUI.  
> Explain terminology and patterns clearly.
>
> ### To do that, follow these steps
> Provide a numbered, human‑oriented procedure:
> - what file to open  
> - what to search for  
> - what to add or modify  
> - what *not* to touch  
> - build/test checkpoints  
> - expected output or behavior  
>
> These steps must be written for a human developer performing the edits manually.
>
> ### Now that those are done, here’s what changed
> Summarize:
> - what files were modified  
> - what new capabilities were introduced  
> - what behavior stayed the same  
> - what this enables in future iterations  
>
> ---
>
> ## Additional requirements
> - Do NOT rewrite or critique the blueprint.  
> - Do NOT add new steps not present in the blueprint.  
> - Expand explanations where helpful for learning.  
> - Maintain strict technical accuracy.  
> - Output a complete, self‑contained Markdown document.  

Each sub‑agent must produce exactly one file:

**Iteration_XX_Developer_Manual.md**

---

## 3. Run all sub‑agents in parallel

- Dispatch all sub‑agents **in parallel**, one per blueprint file.  
- Do not serialize them unless the environment forces you to.  
- Each sub‑agent works only with:
  - `MVVM_Notes.md`  
  - its own `Iteration_XX_Blueprint.md`  

This avoids context overload and cross‑contamination between iterations.

---

## 4. Collect and report results

When all sub‑agents have finished:

1. Confirm which iterations were processed (e.g., `3A`, `3B`, `4`, etc.).  
2. Confirm that each of these files now exists in the workspace:
   - `Iteration_XX_Developer_Manual.md`
3. If any sub‑agent reported a discrepancy between `MVVM_Notes.md` and its blueprint:
   - Surface those discrepancy messages clearly to the user.  
   - Do not attempt to resolve them automatically.

Provide a concise summary like:

- Which iterations succeeded  
- Which iterations are blocked due to discrepancies  
- Where the new Developer Manuals are located  

Do **not** inline the full contents of all manuals unless the user explicitly asks.  
Assume the user will open the generated files directly in their editor.



===============================================

# Developer Manual Agent Prompts  
### (For Iteration_XX Developer Manuals)

This file contains the **three reusable prompts** used to generate, critique, and refine Developer Manuals for each iteration of the MVVM refactor.

All prompts assume:

- `MVVM_Notes.md` is always present in the same directory as the blueprints.  
- The agent (Opus or Sonnet) is running in an environment where all project files are visible (e.g., VS Code GitHub Chat).  
- The user does **not** need to paste any documents; the agent loads them directly from the workspace.  
- The user is **new to MVVM and new to ReactiveUI**.  

## **Iteration Identifier Binding**
At the top of each run, the user will provide a line such as:

**“For this step we are processing Iteration 3A.”**

The agent must:

1. Extract the iteration identifier (`3A`)  
2. Bind it to the placeholder `XX`  
3. Use it for all filenames, including:  
   - `Iteration_3A_Blueprint.md`  
   - `Iteration_3A_Developer_Manual.md`  
   - `Iteration_3A_Developer_Manual_Critique.md`  

If the iteration identifier is missing or ambiguous, the agent must ask the user to clarify.

---

# 1. Opus Prompt — Generate Developer Manual  
**Output file:** `Iteration_XX_Developer_Manual.md`  
**Inputs (loaded automatically from workspace):**  
- `Iteration_XX_Blueprint.md`  
- `MVVM_Notes.md`  

---

## OPUS PROMPT

You are generating a **Developer Manual** from a blueprint.  
The Developer Manual is a *teaching‑oriented*, human‑readable expansion of the blueprint.

At the top of the user message, you will see a line such as:

**“For this step we are processing Iteration 3A.”**

Extract the iteration identifier (`3A`) and bind it to the placeholder `XX`.  
Use this identifier for all filenames and references.

Automatically load the following files from the workspace:

1. **Iteration_XX_Blueprint.md**  
2. **MVVM_Notes.md** (the architectural source of truth)

The user is **new to MVVM and new to ReactiveUI**.  
You must explain concepts, patterns, and terminology accordingly.

---

## Your tasks

### 1. Treat MVVM_Notes.md as the authoritative design document
- The blueprint is a detailed expansion of items in MVVM_Notes.md.  
- The Developer Manual is a teaching expansion of the blueprint.  
- All three documents must remain consistent.

### 2. If you detect any discrepancy between MVVM_Notes.md and the blueprint
- **STOP.**  
- Do **not** assume which document is correct.  
- Do **not** “fix” the blueprint.  
- Instead, output a short message asking the user for clarification.

### 3. If no discrepancies exist, generate the Developer Manual
Produce a complete Markdown document named:

**Iteration_XX_Developer_Manual.md**

For each major section of the blueprint, use this structure:

---

## Section Title (copied from blueprint)

### What we are going to accomplish
Explain:
- the goals of the section  
- the reasoning behind the changes  
- the architectural/MVVM context  
- the ReactiveUI concepts involved  
- why this step appears in this iteration  

Assume the user is new to MVVM and ReactiveUI.  
Explain terminology and patterns clearly.

### To do that, follow these steps
Provide a numbered, human‑oriented procedure:
- what file to open  
- what to search for  
- what to add or modify  
- what *not* to touch  
- build/test checkpoints  
- expected output or behavior  

These steps must be written for a human developer performing the edits manually.

### Now that those are done, here’s what changed
Summarize:
- what files were modified  
- what new capabilities were introduced  
- what behavior stayed the same  
- what this enables in future iterations  

---

## Additional requirements
- Do NOT rewrite or critique the blueprint.  
- Do NOT add new steps not present in the blueprint.  
- Expand explanations where helpful for learning.  
- Maintain strict technical accuracy.  
- Output a complete, self‑contained Markdown document.  



---
---

# 2. Sonnet Prompt — Critique Developer Manual  
**Output file:** `Iteration_XX_Developer_Manual_Critique.md`  
**Inputs (loaded automatically from workspace):**  
- `Iteration_XX_Blueprint.md`  
- `Iteration_XX_Developer_Manual.md`  
- `MVVM_Notes.md`  

---

## SONNET PROMPT

You are validating a Developer Manual generated from a blueprint.

At the top of the user message, you will see a line such as:

**“For this step we are processing Iteration 3A.”**

Extract the iteration identifier (`3A`) and bind it to the placeholder `XX`.  
Use this identifier for all filenames and references.

Automatically load the following files from the workspace:

1. **Iteration_XX_Blueprint.md**  
2. **Iteration_XX_Developer_Manual.md**  
3. **MVVM_Notes.md**

Your output must be a Markdown file named:

**Iteration_XX_Developer_Manual_Critique.md**

---

## Your tasks

### 1. Accuracy Review
Identify any place where the Developer Manual:
- deviates from the blueprint  
- omits required steps  
- adds steps not present in the blueprint  
- contradicts MVVM_Notes.md  
- introduces unsafe or destructive instructions  
- misrepresents technical details  

For each issue, include:
- a short description  
- the section where it occurs  
- why it is a problem  

### 2. Structural Review
Verify that each section includes:
- “What we are going to accomplish”  
- “To do that, follow these steps”  
- “Now that those are done, here’s what changed”  

List any missing or malformed sections.

### 3. Clarity & Teachability Review
Identify:
- unclear instructions  
- ambiguous steps  
- missing build/test checkpoints  
- missing warnings about fragile areas  
- incorrect or misleading explanations  
- places where the manual fails to explain MVVM or ReactiveUI concepts clearly  
  (because the user is new to both)

### 4. Suggested Corrections
For each issue, provide a **specific, actionable correction** that Opus can apply.  
Do **not** rewrite the manual — only describe the corrections.

### 5. Final Verdict
Choose one:

- **READY FOR USE**  
- **NEEDS REVISION** (with reasons)

---

## Important rules
- Do NOT rewrite the Developer Manual.  
- Do NOT rewrite the blueprint.  
- Do NOT generate new content beyond the critique.  
- Output a single Markdown document named `Iteration_XX_Developer_Manual_Critique.md`.

---

# 3. Opus Prompt — Refine Developer Manual Using Critique  
**Output file:** `Iteration_XX_Developer_Manual.md` (revised)  
**Inputs (loaded automatically from workspace):**  
- `Iteration_XX_Blueprint.md`  
- `Iteration_XX_Developer_Manual.md`  
- `Iteration_XX_Developer_Manual_Critique.md`  
- `MVVM_Notes.md`  

---

## OPUS PROMPT

You are revising a Developer Manual based on a critique file.

At the top of the user message, you will see a line such as:

**“For this step we are processing Iteration 3A.”**

Extract the iteration identifier (`3A`) and bind it to the placeholder `XX`.  
Use this identifier for all filenames and references.

Automatically load the following files from the workspace:

1. **Iteration_XX_Blueprint.md**  
2. **Iteration_XX_Developer_Manual.md**  
3. **Iteration_XX_Developer_Manual_Critique.md**  
4. **MVVM_Notes.md**

The user is **new to MVVM and new to ReactiveUI**.  
Ensure the revised manual reflects that.

---

## Your tasks

### 1. Apply all valid corrections
For each issue listed in the critique:
- fix the problem in the Developer Manual  
- preserve the blueprint’s intent and scope  
- maintain technical accuracy  
- keep the manual safe for a human developer  

### 2. Preserve the required structure
Each section must contain:
- “What we are going to accomplish”  
- “To do that, follow these steps”  
- “Now that those are done, here’s what changed”  

### 3. Do not introduce new steps
Only include steps that appear in the blueprint.

### 4. Improve clarity and teachability
Where the critique identifies unclear or ambiguous instructions:
- rewrite them for clarity  
- add missing warnings or checkpoints  
- ensure the manual is easy to follow  
- expand explanations of MVVM and ReactiveUI concepts when needed  

### 5. Produce a complete Markdown document
Output a fully revised:

**Iteration_XX_Developer_Manual.md**

It must be self‑contained and readable without the critique.

---

