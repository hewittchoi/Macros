using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Net;
using System.Linq;
using System.IO;

using SwissAcademic.Citavi;
using SwissAcademic.Citavi.Metadata;
using SwissAcademic.Citavi.Shell;
using SwissAcademic.Collections;
using SwissAcademic.Citavi.DataExchange;

// Implementation of macro editor is preliminary and experimental.
// The Citavi object model is subject to change in future version.

public static class CitaviMacro
{
    public static void Main()
    {
        //****************************************************************************************************************
        // EXPORT ATTACHMENTS TO CATEGORY FOLDER STRUCTURE
        // V1.5 -- 2019-05-29   - option for creating Location in Citavi for each exported file
        // v1.6 -- 2019-06-04   - folder for attachments without categories

        // EDIT HERE
        // Variables to be changed by user

        bool createFoldersForAllCategories = true; // set to false if only category folders for references with attachments are required
        bool createLocationForExportedFiles = true; // create a new Location in the Citavi project that points to the exported file 
                                                    // local projects only

        string noCategoryFolder = "0 No Category"; // Name of the folder for attachments without categories
                                                    
        // DO NOT EDIT BELOW THIS LINE
        // ****************************************************************************************************************

                if (Program.ProjectShells.Count == 0) return;       //no project open

        int foundCounter = 0;
        int changeCounter = 0;
        int errorCounter = 0;

        //iterate over all references in the current filter (or over all, if there is no filter)
        List<Reference> references = Program.ActiveProjectShell.PrimaryMainForm.GetFilteredReferences();
        if (references == null) return;

        //reference to active Project
        Project activeProject = Program.ActiveProjectShell.Project;
        bool isCloudProject = activeProject.DesktopProjectConfiguration.ProjectType == ProjectType.DesktopCloud;

        if (activeProject == null) return;

        //get root folder to export to 
        string exportPath;
        FolderBrowserDialog folderDialog = new FolderBrowserDialog();
        folderDialog.Description = "Please select root folder for export";
        folderDialog.SelectedPath = System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (folderDialog.ShowDialog() == DialogResult.OK)
            exportPath = folderDialog.SelectedPath;
        else
            return;


        // create category structure as folder structure if all categories are required

        if (createFoldersForAllCategories)
        {
            DebugMacro.WriteLine("Creating category folder structure ...");

            List<Category> allCategories = activeProject.AllCategories.ToList();
            List<string> allCategoryPaths = new List<string>();

            foreach (Category category in allCategories)
            {
                string[] categoryPaths = category.GetPath(true).Split(new string[] { " > " }, StringSplitOptions.None);
                for (int i = 0; i < categoryPaths.Length; i++)
                {
                    categoryPaths[i] = MakeValidFileName(categoryPaths[i]);
                }
                string categoryPath = String.Join(@"\", categoryPaths);
                allCategoryPaths.Add(categoryPath);
            }

            foreach (string categoryPath in allCategoryPaths)
            {
                string fullPath = exportPath + @"\" + categoryPath;
                CreateFolderStructure(fullPath);
            }

        }

        // export reference Attachments

        foreach (Reference reference in references)
        {
            DebugMacro.WriteLine("Processing references ...");
            DebugMacro.WriteLine("Processing " + reference.ShortTitle + " ... ");

            //establish whether or not there are ATTACHMENTS
            List<Location> referenceAttachments = new List<Location>();
            ReferenceLocationCollection referenceLocations = reference.Locations;
            foreach (Location location in referenceLocations)
            {
                if (location.LocationType == LocationType.ElectronicAddress &&
						(location.Address.LinkedResourceType == LinkedResourceType.AttachmentRemote ||
                        location.Address.LinkedResourceType == LinkedResourceType.AttachmentFile ||
						 location.Address.LinkedResourceType == LinkedResourceType.AbsoluteFileUri ||
						 location.Address.LinkedResourceType == LinkedResourceType.RelativeFileUri))		
					
                    referenceAttachments.Add(location);
            }
            if (referenceAttachments == null || referenceAttachments.Count == 0) continue;

            DebugMacro.WriteLine("Number of attachments found: " + referenceAttachments.Count.ToString());

            //establish Category tree
            List<string> referenceCategoryPaths = new List<string>();
            foreach (Category category in reference.Categories)
            {
                string[] categoryPaths = category.GetPath(true).Split(new string[] { " > " }, StringSplitOptions.None);
                for (int i = 0; i < categoryPaths.Length; i++)
                {
                    categoryPaths[i] = MakeValidFileName(categoryPaths[i]);
                }
                string categoryPath = String.Join(@"\", categoryPaths);
                referenceCategoryPaths.Add(categoryPath);
            }
            if (!referenceCategoryPaths.Any())
                referenceCategoryPaths.Add(noCategoryFolder);

            // create folders if necessary ...

            foreach (string referenceCategoryPath in referenceCategoryPaths)
            {

                string fullPath = exportPath + @"\" + referenceCategoryPath;

                CreateFolderStructure(fullPath);

                // ... and export attachments

                foreach (Location referenceAttachment in referenceAttachments)
                {
                    //string sourcePath = referenceAttachment.AddressUri.AbsoluteUri.GetLocalPathSafe();
					
					Uri sourceUri = referenceAttachment.Address.Resolve();
					string sourcePath = sourceUri.LocalPath;
                    string destinationPath = String.Empty;
					
					if (referenceAttachment.Address.LinkedResourceType == LinkedResourceType.AttachmentRemote)
                    {
                        if (referenceAttachment.Address.LinkedResourceStatus != LinkedResourceStatus.Attached) continue;
                        if (referenceAttachment.Address.CachingStatus != CachingStatus.Available) continue;					
						
                        destinationPath = fullPath + @"\" + referenceAttachment.FullName;
                    }
                    else
                    {
                        destinationPath = fullPath + @"\" + Path.GetFileName(sourcePath);
                    }    

                    DebugMacro.WriteLine("Copying " + sourcePath + " --> " + destinationPath);

                    //check if source exists
                    if (!File.Exists(sourcePath))
                    {
                        DebugMacro.WriteLine("Source file not found.");
                        errorCounter++;
                        continue;
                    }

                    bool tryAgain = true;
                    bool success = false;
                    while (tryAgain)
                    {
                        try
                        {
                            File.Copy(sourcePath, destinationPath, true);
                            changeCounter++;
                            tryAgain = false;
                            success = true;
                        }
                        catch (Exception e)
                        {
                            tryAgain = false;
                            DialogResult directoryError = MessageBox.Show("An error occurred creating a folder: " + e.Message,
                                                        "Error creating folder", MessageBoxButtons.AbortRetryIgnore,
                                                        MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);

                            if (directoryError == DialogResult.Abort) return;
                            else if (directoryError == DialogResult.Retry) tryAgain = true;
                            else
                            {
                                errorCounter++;
                                break;
                            };
                        }
                    }

                    if (createLocationForExportedFiles && success && !isCloudProject)
                        CreateLocation(reference, destinationPath);

                }
            }
		}

		

        // Message upon completion
        string message = "Finished.\n {0} files copied\n {1} thought files created\n {2} errors occurred";
        message = string.Format(message, changeCounter.ToString(), foundCounter.ToString(), errorCounter.ToString());
        MessageBox.Show(message, "Macro", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // Ask whether backup is available

    private static void CreateLocation(Reference reference, string path)
    {
        Location location = new Location(reference, LocationType.ElectronicAddress, path);
        reference.Locations.Add(location);
        return;
    }

    private static void CreateFolderStructure(string fullPath)
    {
        if (!Directory.Exists(fullPath))
        {
            bool tryAgain = true;
            while (tryAgain)
            {
                try
                {
                    DirectoryInfo di = Directory.CreateDirectory(fullPath);
                    DebugMacro.WriteLine("Folder created: " + di.FullName);
                    tryAgain = false;
                }
                catch (Exception e)
                {
                    tryAgain = false;
                    DialogResult directoryError = MessageBox.Show("An error occurred creating a folder: " + e.Message,
                                                "Error creating folder", MessageBoxButtons.AbortRetryIgnore,
                                                MessageBoxIcon.Error, MessageBoxDefaultButton.Button1);

                    if (directoryError == DialogResult.Abort) return;
                    else if (directoryError == DialogResult.Retry) tryAgain = true;
                    else
                    {
                        break;
                    };
                }
            }
        }
    }

    private static bool IsBackupAvailable()
    {
        string warning = String.Concat("Important: This macro will make irreversible changes to your project.",
            "\r\n\r\n", "Make sure you have a current backup of your project before you run this macro.",
            "\r\n", "If you aren't sure, click Cancel and then, in the main Citavi window, on the File menu, click Create backup.",
            "\r\n\r\n", "Do you want to continue?"
        );

        return (MessageBox.Show(warning, "Citavi", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) == DialogResult.OK);
    }

    public static string MakeValidFileName(this string name)
    {

        char[] invalids = System.IO.Path.GetInvalidFileNameChars();
        string validName = String.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        return validName;
    }
}