using System;
using System.Collections.Generic;

namespace Ongenet.App.Services;

/// <summary>App-wide library favourites and user-owned categories (instruments, effects, files, folders, …).</summary>
public interface ILibraryOrganizationService
{
    IReadOnlyList<string> Favourites { get; }
    IReadOnlyList<LibraryCategoryDto> Categories { get; }

    bool IsFavourite(string itemKey);
    void ToggleFavourite(string itemKey);
    void SetFavourite(string itemKey, bool favourite);

    LibraryCategoryDto CreateCategory(string name);
    void RenameCategory(Guid id, string name);
    void DeleteCategory(Guid id);
    void AddToCategory(Guid categoryId, string itemKey);
    void RemoveFromCategory(Guid categoryId, string itemKey);
    bool IsInCategory(Guid categoryId, string itemKey);

    /// <summary>Raised after favourites or categories change (already persisted).</summary>
    event Action? Changed;
}
