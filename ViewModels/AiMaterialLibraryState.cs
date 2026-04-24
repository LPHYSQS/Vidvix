using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Vidvix.Utils;

namespace Vidvix.ViewModels;

public readonly record struct AiMaterialImportResult(
    int AddedCount,
    int DuplicateCount);

public sealed class AiMaterialLibraryState : ObservableObject
{
    private AiMaterialItemViewModel? _selectedMaterial;

    public ObservableCollection<AiMaterialItemViewModel> Materials { get; } = new();

    public AiMaterialItemViewModel? SelectedMaterial
    {
        get => _selectedMaterial;
        set
        {
            if (value is null && Materials.Count > 0)
            {
                value = Materials[0];
            }

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

            Materials.Add(new AiMaterialItemViewModel(inputPath));
            addedCount++;
        }

        if (SelectedMaterial is null && Materials.Count > 0)
        {
            SelectedMaterial = Materials[0];
        }
        else
        {
            UpdateCollectionStateProperties();
            UpdateSelectionState();
        }

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

        AiMaterialItemViewModel? replacementSelection = null;
        if (ReferenceEquals(material, SelectedMaterial))
        {
            replacementSelection = index < Materials.Count - 1
                ? Materials[index + 1]
                : index > 0
                    ? Materials[index - 1]
                    : null;
        }

        Materials.RemoveAt(index);

        if (ReferenceEquals(material, SelectedMaterial))
        {
            _selectedMaterial = replacementSelection;
            OnPropertyChanged(nameof(SelectedMaterial));
            UpdateSelectionState();
        }
        else
        {
            UpdateCollectionStateProperties();
        }

        return true;
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
