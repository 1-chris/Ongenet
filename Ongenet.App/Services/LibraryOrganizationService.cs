using System;
using System.Collections.Generic;
using System.Linq;

namespace Ongenet.App.Services;

/// <summary>Persists library favourites/categories on <see cref="AppSettings"/> via <see cref="IAppSettingsService"/>.</summary>
public sealed class LibraryOrganizationService : ILibraryOrganizationService
{
    private readonly IAppSettingsService _settings;

    public LibraryOrganizationService(IAppSettingsService settings) => _settings = settings;

    public IReadOnlyList<string> Favourites => _settings.Current.LibraryFavourites;

    public IReadOnlyList<LibraryCategoryDto> Categories => _settings.Current.LibraryCategories;

    public event Action? Changed;

    public bool IsFavourite(string itemKey)
        => !string.IsNullOrEmpty(itemKey) && _settings.Current.LibraryFavourites.Contains(itemKey);

    public void ToggleFavourite(string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey)) return;
        SetFavourite(itemKey, !IsFavourite(itemKey));
    }

    public void SetFavourite(string itemKey, bool favourite)
    {
        if (string.IsNullOrEmpty(itemKey)) return;
        var list = _settings.Current.LibraryFavourites;
        var has = list.Contains(itemKey);
        if (favourite == has) return;
        if (favourite) list.Add(itemKey);
        else list.Remove(itemKey);
        Persist();
    }

    public LibraryCategoryDto CreateCategory(string name)
    {
        var cat = new LibraryCategoryDto
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? "Category" : name.Trim()
        };
        _settings.Current.LibraryCategories.Add(cat);
        Persist();
        return cat;
    }

    public void RenameCategory(Guid id, string name)
    {
        var cat = Find(id);
        if (cat is null) return;
        cat.Name = string.IsNullOrWhiteSpace(name) ? cat.Name : name.Trim();
        Persist();
    }

    public void DeleteCategory(Guid id)
    {
        var list = _settings.Current.LibraryCategories;
        var i = list.FindIndex(c => c.Id == id);
        if (i < 0) return;
        list.RemoveAt(i);
        Persist();
    }

    public void AddToCategory(Guid categoryId, string itemKey)
    {
        if (string.IsNullOrEmpty(itemKey)) return;
        var cat = Find(categoryId);
        if (cat is null || cat.ItemKeys.Contains(itemKey)) return;
        cat.ItemKeys.Add(itemKey);
        Persist();
    }

    public void RemoveFromCategory(Guid categoryId, string itemKey)
    {
        var cat = Find(categoryId);
        if (cat is null) return;
        if (!cat.ItemKeys.Remove(itemKey)) return;
        Persist();
    }

    public bool IsInCategory(Guid categoryId, string itemKey)
    {
        var cat = Find(categoryId);
        return cat is not null && cat.ItemKeys.Contains(itemKey);
    }

    private LibraryCategoryDto? Find(Guid id)
        => _settings.Current.LibraryCategories.FirstOrDefault(c => c.Id == id);

    private void Persist()
    {
        _settings.CaptureAndSave();
        Changed?.Invoke();
    }
}
