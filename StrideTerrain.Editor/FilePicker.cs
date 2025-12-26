using Hexa.NET.ImGui;
using Silk.NET.SDL;
using System;
using System.IO;
using System.Numerics;
using static Hexa.NET.ImGui.ImGui;

namespace StrideTerrain.Editor;
public class FilePicker(string id, bool isOpen)
{
    public string CurrentFolder { get; set; } = AppContext.BaseDirectory;
    public string? SelectedFile { get; set; }
    public string Filter { get; set; } = "*.json";

    private string _selectedFileName = "";

    public void Show()
    {
        OpenPopup(id);
    }

    public bool Draw()
    {
        var result = false;
        if (BeginPopupModal(id, ImGuiWindowFlags.NoTitleBar))
        {
            Text("Current Folder: " + CurrentFolder);

            if (BeginChild(1, new Vector2(0, 600)))
            {
                var di = new DirectoryInfo(CurrentFolder);
                if (di.Exists)
                {
                    if (di.Parent != null)
                    {
                        PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
                        if (Selectable("../", false, ImGuiSelectableFlags.NoAutoClosePopups))
                        {
                            CurrentFolder = di.Parent.FullName;
                        }
                        PopStyleColor();
                    }
                    foreach (var fse in Directory.GetDirectories(di.FullName))
                    {
                        string name = Path.GetFileName(fse);
                        PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
                        if (Selectable(name + "/", false, ImGuiSelectableFlags.NoAutoClosePopups))
                        {
                            CurrentFolder = fse;
                        }
                        PopStyleColor();
                    }

                    foreach (var fse in Directory.GetFiles(di.FullName, Filter))
                    {
                        string name = Path.GetFileName(fse);
                        bool isSelected = SelectedFile == fse;
                        if (Selectable(name, isSelected, ImGuiSelectableFlags.NoAutoClosePopups))
                        {
                            SelectedFile = fse;
                            _selectedFileName = Path.GetFileName(fse);
                            //if (returnOnSelection)
                            {
                                result = false;
                            }
                        }
                        if (IsMouseDoubleClicked(0))
                        {
                            result = true;
                            CloseCurrentPopup();
                        }
                    }
                }

            }
            EndChild();

            if (!isOpen)
            {
                InputText("Name", ref _selectedFileName, 99);
            }

            if (Button("Cancel"))
            {
                result = false;
                CloseCurrentPopup();
            }

            if (isOpen)
            {
                if (SelectedFile != null)
                {
                    SameLine();
                    if (Button("Open"))
                    {
                        result = true;
                        CloseCurrentPopup();
                    }
                }
            }
            else
            {
                if (_selectedFileName != null && _selectedFileName.Length > 0)
                {
                    SameLine();
                    if (Button("Save"))
                    {
                        SelectedFile = Path.Combine(CurrentFolder, _selectedFileName);
                        if (!SelectedFile.EndsWith(".json"))
                        {
                            SelectedFile += ".json";
                        }

                        result = true;
                        CloseCurrentPopup();
                    }
                }
            }

            EndPopup();
        }

        return result;
    }
}
