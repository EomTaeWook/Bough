# Bough

[한국어](README.md) | **English**

**Bough is a Git GUI for macOS and Windows that makes merge conflicts easier to understand and resolve.**

It aims to keep everyday Git work light and fast while making the context and outcome of conflict resolution clear.

## Download

[Download Bough v0.1.0-beta.1 for Windows x64](https://github.com/EomTaeWook/Bough/releases/download/v0.1.0-beta.1/Bough-v0.1.0-beta.1-win-x64.zip) · [Release notes](https://github.com/EomTaeWook/Bough/releases/tag/v0.1.0-beta.1)

Extract the archive and run `Bough.App.exe`. Git must be installed; a separate .NET installation is not needed. This beta provides a Windows x64 package. On macOS, use the source build instructions below.

## Preview

### Conflict resolution

![Bough conflict window showing both changes, individual and batch choices, and the final file preview](Docs/images/conflict.png)

This screen shows a reproduced text merge conflict. Compare both changes, choose how to resolve each section, and review the final file.

### History

![Bough History showing the commit graph, changed files, and code preview](Docs/images/history.png)

## Why Bough?

Conflict resolution in existing Git GUIs can be difficult when:

- It is unclear where each side of a conflict came from.
- There is too little context to decide which changes to keep.
- The effect of a choice on the final file is hard to see immediately.
- Repository tabs pile up across the top, making it harder to see where you are and switch repositories.

Bough is designed to show the source of each conflicting change and preview the resulting file as you make selections.

## Core experience

- **Clear change origins:** Distinguish the branches, commits, and other context behind each change.
- **Readable conflict comparison:** See conflicting sections alongside surrounding code.
- **Immediate result preview:** See how each choice changes the final file.
- **Review before applying:** Check the result before saving the file and continuing your Git work.
- **Clear repository navigation:** See and switch the current repository without relying on a row of tabs.

## Features

- **Repository cloning:** Clone a remote URL or local path into a new folder, then add and open it from the recent repository list.
- **Local changes:** Inspect changed files and diffs with line numbers, stage and unstage, commit, discard changes, stop tracking files, and ignore untracked files.
- **History and references:** Browse the commit graph and file changes; create, switch, and delete branches; create tags.
- **Remotes and stashes:** Fetch, Pull, and Push; configure the default Pull strategy or choose one for a single Pull; save, apply, and delete stashes.
- **Conflict resolution:** Inspect merge or rebase changes, choose individual sections or apply one choice to the remaining sections, edit and preview the result, then save and stage it or continue a rebase.

Features and workflows are still being refined.

Additional screens and workflows are described in the [design notes](Design/README.md) (Korean).

## Technology

- **Language:** C# / .NET
- **Platforms:** macOS and Windows
- **UI:** Avalonia

The project is under active development. [Implementation status](Docs/CurrentStatus.md) (Korean) separates available features from checks still pending. See [development docs](Docs/README.md) and [design notes](Design/README.md) for more detail.

## Run

You need the .NET 10 SDK and Git.

```powershell
dotnet run --project Bough.App/Bough.App.csproj
```

Use the **+** menu beside the Repositories heading in the left sidebar to add an existing local repository or clone one from a remote URL or local path. Enter the destination folder path, or choose an empty folder with Browse; Bough adds and opens the repository when cloning finishes. If the repository has unresolved conflicts, choose a change for each section, inspect or edit the final file, and select **Save and Stage**. Text conflict resolution currently targets UTF-8 files.

Select **History** at the top to see commits from all local and remote references in a branch graph. Selecting a commit shows its author, date, message, and changed files in the details panel. The first 200 commits are shown; scroll down to load older history. Selecting and inspecting a commit does not change the repository.

Right-click a branch under **Git references** on the left to delete a local or remote branch. Confirm the target before deletion. The checked-out branch and a remote's default branch cannot be deleted, and local branches with unmerged commits are kept. Deleting a remote branch affects the server and other users.

## Data generation

`Excel/String.xlsx` is intended as the source of UI strings, and the app reads `Datas/String.json`. Existing entries in the two files are not fully synchronized. Before regenerating all data, review the synchronization note in the [data conversion guide](<Docs/데이터 변환 도구 사용법.md>) (Korean).

`JsonToCSharp.exe` is managed with Git LFS. If the executable is missing after cloning the repository, install Git LFS and run `git lfs pull` from the repository root.

```powershell
cd ExportTools/ExcelToJson
./ExcelToJson.exe --no-pause
cd ../JsonToCSharp
./JsonToCSharp.exe --no-pause
```

The bundled converter may exit with a `Console.ReadKey` exception after generating the files. If that happens, check both the completion message and changes to the generated output. UI text is read from `StringTemplate`: Korean is used when the operating system UI language is Korean, and English is used otherwise.

## License

[Bough Source License 1.0](LICENSE) allows free personal, educational, and internal business use and modification. You may redistribute it without charge if you retain the license and copyright notice. Selling Bough or a substantially Bough-based Git client, including a renamed copy, is restricted. Third-party components remain under their own licenses.
