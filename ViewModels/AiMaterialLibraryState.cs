using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Vidvix.Core.Interfaces;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public readonly record struct AiMaterialImportResult(
    int AddedCount,
    int DuplicateCount);

public sealed class AiMaterialLibraryState : ObservableObject
{
    private readonly ILocalizationService? _localizationService;
    private AiMaterialItemViewModel? _selectedMaterial;

    public AiMaterialLibraryState(ILocalizationService? localizationService = null)
    {
        _localizationService = localizationService;
    }

    public ObservableCollection<AiMaterialItemViewModel> Materials { get; } = new();

    public AiMaterialItemViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (value is not null && !Materials.Contains(value))
            {
                return;
            }

            if (ReferenceEquals(_selectedMaterial, value))
            {
                return;
            }

            _selectedMaterial = value;
            OnPropertyChanged();
            UpdateSelectionState();
        }
    }

    public bool HasMaterials => Materials.Count > 0;

    public bool HasNoMaterials => !HasMaterials;

    public int MaterialCount => Materials.Count;

    public AiMaterialImportResult AddMaterials(IEnumerable<string> inputPaths)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);

        var knownPaths = new HashSet<string>(
            Materials.Select(item => item.InputPath),
            StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var inputPath in inputPaths
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!knownPaths.Add(inputPath))
            {
                duplicateCount++;
                continue;
            }

            Materials.Add(new AiMaterialItemViewModel(inputPath, localizationService: _localizationService));
            addedCount++;
        }

        UpdateSelectionState();

        return new AiMaterialImportResult(addedCount, duplicateCount);
    }

    public AiMaterialImportResult AddMaterials(IEnumerable<AiMaterialItemViewModel> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        var knownPaths = new HashSet<string>(
            Materials.Select(item => item.InputPath),
            StringComparer.OrdinalIgnoreCase);
        var currentBatchPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var addedCount = 0;
        var duplicateCount = 0;

        foreach (var material in materials)
        {
            if (material is null)
            {
                continue;
            }

            if (!currentBatchPaths.Add(material.InputPath) || !knownPaths.Add(material.InputPath))
            {
                duplicateCount++;
                continue;
            }

            Materials.Add(material);
            addedCount++;
        }

        UpdateSelectionState();

        return new AiMaterialImportResult(addedCount, duplicateCount);
    }

    public bool RemoveMaterial(AiMaterialItemViewModel material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var index = Materials.IndexOf(material);
        if (index < 0)
        {
            return false;
        }

        var wasSelected = ReferenceEquals(material, _selectedMaterial);

        Materials.RemoveAt(index);

        if (wasSelected)
        {
            _selectedMaterial = null;
            OnPropertyChanged(nameof(SelectedMaterial));
        }

        UpdateSelectionState();
        return true;
    }

    public void RefreshLocalization()
    {
        foreach (var material in Materials)
        {
            material.RefreshLocalization();
        }
    }

    private void UpdateSelectionState()
    {
        foreach (var material in Materials)
        {
            material.IsActive = ReferenceEquals(material, _selectedMaterial);
        }

        UpdateCollectionStateProperties();
    }

    private void UpdateCollectionStateProperties()
    {
        OnPropertyChanged(nameof(HasMaterials));
        OnPropertyChanged(nameof(HasNoMaterials));
        OnPropertyChanged(nameof(MaterialCount));
    }
}
