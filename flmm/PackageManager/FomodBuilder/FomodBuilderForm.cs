﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows.Forms;
using System.Xml;
using Fomm.Controls;
using Fomm.PackageManager.Controls;
using Fomm.Properties;
using Fomm.Util;
using GeMod.Interface;

namespace Fomm.PackageManager.FomodBuilder
{
  /// <summary>
  ///   This form builds a FOMod form existing files.
  /// </summary>
  public partial class FomodBuilderForm : Form
  {
    /// <summary>
    ///   The possible validation states of the form.
    /// </summary>
    protected enum ValidationState
    {
      /// <summary>
      ///   Indicates there are no errors or warnings.
      /// </summary>
      Passed,

      /// <summary>
      ///   Indicates there are warnings.
      /// </summary>
      /// <remarks>
      ///   Warnings are non-fatal errors.
      /// </remarks>
      Warnings,

      /// <summary>
      ///   Indicates there are errors.
      /// </summary>
      Errors
    }

    private ReadmeGeneratorForm m_rgdGenerator = new ReadmeGeneratorForm();
    private bool m_booInfoEntered;
    private bool m_booLoadedInfo;

    #region Properties

    /// <summary>
    ///   Gets the path of the fomod that was built.
    /// </summary>
    /// <remarks>
    ///   This value will be <lang langref="null" /> if the fomod was not successfully built.
    /// </remarks>
    /// <value>The path of the fomod that was built.</value>
    public string FomodPath { get; private set; }

    #endregion

    #region Constructors

    /// <summary>
    ///   The default constructor.
    /// </summary>
    public FomodBuilderForm()
    {
      InitializeComponent();

      Icon = Resources.fomm02;
      Settings.Default.windowPositions.GetWindowPosition("FomodBuilderForm", this);

      tbxPFPPath.DataBindings.Add("Enabled", cbxPFP, "Checked");
      butSelectPFPFolder.DataBindings.Add("Enabled", cbxPFP, "Checked");
      fseScriptEditor.DataBindings.Add("Enabled", cbxUseScript, "Checked");
      tbxPFPPath.Text = Settings.Default.pfpOutputPath;
    }

    /// <summary>
    ///   The PFP edit constructor.
    /// </summary>
    /// <param name="p_pfpPack">The PFP to edit.</param>
    /// <param name="p_strSourcesPath">The path to the directory contains the required source files.</param>
    public FomodBuilderForm(PremadeFomodPack p_pfpPack, string p_strSourcesPath)
      : this()
    {
      var lstCopyInstructions = p_pfpPack.GetCopyInstructions(p_strSourcesPath);
      var strPremadeSource = Archive.GenerateArchivePath(p_pfpPack.PFPPath, p_pfpPack.PremadePath);
      lstCopyInstructions.Add(new KeyValuePair<string, string>(strPremadeSource, "/"));

      var lstSourceFiles = p_pfpPack.GetSources();
      lstSourceFiles.ForEach(s =>
      {
        s.Source = Path.Combine(p_strSourcesPath, s.Source);
      });
      lstSourceFiles.Add(new SourceFile(p_pfpPack.PFPPath, null, true, false, false));

      ffsFileStructure.SetCopyInstructions(lstSourceFiles, lstCopyInstructions);
      tbxFomodFileName.Text = p_pfpPack.FomodName;
      sdsDownloadLocations.DataSource = lstSourceFiles;
      cbxPFP.Checked = true;
      tbxPFPPath.Text = Path.GetDirectoryName(p_pfpPack.PFPPath);
      tbxHowTo.Text = p_pfpPack.GetCustomHowToSteps();
    }

    #endregion

    /// <summary>
    ///   Raises the <see cref="Form.Closing" /> event.
    /// </summary>
    /// <remarks>
    ///   Saves the window's position.
    /// </remarks>
    /// <param name="e">A <see cref="CancelEventArgs" /> describing the event arguments.</param>
    protected override void OnClosing(CancelEventArgs e)
    {
      Settings.Default.windowPositions.SetWindowPosition("FomodBuilderForm", this);
      Settings.Default.Save();
      base.OnClosing(e);
    }

    #region Navigation

    /// <summary>
    ///   Handles the <see cref="VerticalTabControl.SelectedTabPageChanged" /> event of the main
    ///   navigation tab control.
    /// </summary>
    /// <remarks>
    ///   This handles initialization of tabs as the selected tab changes.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">A <see cref="VerticalTabControl.TabPageEventArgs" /> describing the event arguments.</param>
    private void vtcFomodData_SelectedTabPageChanged(object sender, VerticalTabControl.TabPageEventArgs e)
    {
      if (e.TabPage == vtpDownloadLocations)
      {
        UpdateDownloadLocationsList();
      }
      else if (e.TabPage == vtpReadme)
      {
        SetReadmeDefault();
      }
      else if (e.TabPage == vtpScript)
      {
        SetScriptDefault();
      }
      else if (e.TabPage == vtpInfo)
      {
        m_booInfoEntered = true;
        SetInfoDefault();
      }
    }

    /// <summary>
    ///   Handles the <see cref="Control.Click" /> event of the OK button.
    /// </summary>
    /// <remarks>
    ///   This ensures that the information is valid before creating the FOMod/Premade FOMod Pack.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">A <see cref="EventArgs" /> describing the event arguments.</param>
    private void butOK_Click(object sender, EventArgs e)
    {
      switch (PerformValidation())
      {
        case ValidationState.Errors:
          MessageBox.Show(this, "You must correct the errors before saving.", "Error", MessageBoxButtons.OK,
                          MessageBoxIcon.Error);
          return;
        case ValidationState.Warnings:
          if (
            MessageBox.Show(this,
                            "There are warnings." + Environment.NewLine +
                            "Warnings can be ignored, but they can indicate missing information that you meant to enter." +
                            Environment.NewLine + "Would you like to continue?", "Warning", MessageBoxButtons.OKCancel,
                            MessageBoxIcon.Warning) == DialogResult.Cancel)
          {
            return;
          }
          break;
        case ValidationState.Passed:
          break;
        default:
          throw new InvalidEnumArgumentException("Unexpected value for ValidationState enum.");
      }

      var rmeReadme = redReadmeEditor.Readme;
      var fscScript = fseScriptEditor.Script;
      var xmlInfo = m_booInfoEntered ? fomod.SaveInfo(finInfo) : null;

      var crsOldCursor = Cursor;
      Cursor = Cursors.WaitCursor;
      var lstCopyInstructions = ffsFileStructure.GetCopyInstructions();
      Cursor = crsOldCursor;

      if (cbxFomod.Checked)
      {
        var fgnGenerator = new NewFomodBuilder();
        FomodPath = fgnGenerator.BuildFomod(tbxFomodFileName.Text, lstCopyInstructions, rmeReadme, xmlInfo,
                                            m_booInfoEntered, finInfo.Screenshot, fscScript);
        if (String.IsNullOrEmpty(FomodPath))
        {
          return;
        }
      }
      if (cbxPFP.Checked)
      {
        Settings.Default.pfpOutputPath = tbxPFPPath.Text;
        Settings.Default.Save();

        var strVersion = "1.0";
        var strMachineVersion = "1.0";
        var xmlInfoTmp = fomod.SaveInfo(finInfo);
        if (xmlInfoTmp != null)
        {
          var xndVersion = xmlInfoTmp.SelectSingleNode("/fomod/Version");
          if (xndVersion != null)
          {
            strVersion = xndVersion.InnerText;
            var xatVersion = xndVersion.Attributes["MachineVersion"];
            if (xatVersion != null)
            {
              strMachineVersion = xatVersion.Value;
            }
          }
        }
        var fpbPackBuilder = new PremadeFomodPackBuilder();
        var strPFPPAth = fpbPackBuilder.BuildPFP(tbxFomodFileName.Text, strVersion, strMachineVersion,
                                                 lstCopyInstructions, sdsDownloadLocations.DataSource, tbxHowTo.Text,
                                                 rmeReadme, xmlInfo, m_booInfoEntered, finInfo.Screenshot, fscScript,
                                                 tbxPFPPath.Text);
        if (String.IsNullOrEmpty(strPFPPAth))
        {
          return;
        }
      }
      DialogResult = DialogResult.OK;
    }

    #endregion

    #region Validation

    /// <summary>
    ///   Validates the data on this form.
    /// </summary>
    /// <remarks>
    ///   This method validates the form data, and displays any errors or warnings.
    /// </remarks>
    /// <returns>The currnt validation state of the form's data.</returns>
    protected ValidationState PerformValidation()
    {
      var booHasErrors = false;
      var booHasWarnings = false;
      sspError.Clear();
      sspWarning.Clear();

      //Source Tab Validation
      if (!ValidateSources())
      {
        sspError.SetStatus(vtpSources, "Missing required information.");
        booHasErrors = true;
      }

      //download locations tab validation
      UpdateDownloadLocationsList();
      var lstSourceFiles = sdsDownloadLocations.DataSource;
      foreach (var sflLocation in lstSourceFiles)
      {
        if (String.IsNullOrEmpty(sflLocation.URL) && !sflLocation.Included)
        {
          if (cbxPFP.Checked)
          {
            sspError.SetStatus(vtpDownloadLocations, "Download locations not specified for all sources.");
            booHasErrors = true;
          }
          else
          {
            sspWarning.SetStatus(vtpDownloadLocations, "Download locations not specified for all sources.");
            booHasWarnings = true;
          }
          break;
        }
      }

      //readme tab validation
      SetReadmeDefault();
      if (redReadmeEditor.Readme == null)
      {
        sspWarning.SetStatus(vtpReadme, "No Readme file present.");
        booHasWarnings = true;
      }

      //fomod info Validation
      SetInfoDefault();
      if (!finInfo.PerformValidation())
      {
        sspError.SetStatus(vtpInfo, "Invalid information.");
        booHasErrors = true;
      }
      else if (String.IsNullOrEmpty(finInfo.Name) ||
               String.IsNullOrEmpty(finInfo.Author) ||
               String.IsNullOrEmpty(finInfo.HumanReadableVersion) ||
               String.IsNullOrEmpty(finInfo.Website) ||
               String.IsNullOrEmpty(finInfo.Description))
      {
        sspWarning.SetStatus(vtpInfo, "Missing information.");
        booHasWarnings = true;
      }

      //script validation
      SetScriptDefault();
      if (cbxUseScript.Checked)
      {
        if (fseScriptEditor.Script == null)
        {
          sspWarning.SetStatus(vtpScript, "Missing script.");
          booHasWarnings = true;
        }
        else if (!fseScriptEditor.IsValid)
        {
          sspError.SetStatus(vtpScript, "Invalid script.");
          booHasErrors = true;
        }
      }

      //save location validation
      if (!cbxFomod.Checked && !cbxPFP.Checked)
      {
        sspError.SetError(vtpOutput, "No items selected for creation.");
        booHasErrors = true;
      }
      else if (!ValidatePFPSavePath())
      {
        sspError.SetError(vtpOutput, "Premade FOMod Pack save location is required.");
        booHasErrors = true;
      }

      if (booHasErrors)
      {
        return ValidationState.Errors;
      }
      if (booHasWarnings)
      {
        return ValidationState.Warnings;
      }
      return ValidationState.Passed;
    }

    #region Sources Tab

    /// <summary>
    ///   Validates the source files of the FOMod.
    /// </summary>
    /// <returns>
    ///   <lang langref="true" /> if the user has entered a file name for the FOMod, and selected
    ///   files to include; <lang langref="false" /> otherwise.
    /// </returns>
    protected bool ValidateSources()
    {
      var booPassed = ValidateFomodFileName();
      booPassed &= ValidateFomodFiles();
      return booPassed;
    }

    /// <summary>
    ///   Ensures that the user has entered a file name.
    /// </summary>
    /// <returns><lang langref="true" /> if the user has entered a file name; <lang langref="false" /> otherwise.</returns>
    protected bool ValidateFomodFileName()
    {
      sspError.SetError(tbxFomodFileName, null);
      if (String.IsNullOrEmpty(tbxFomodFileName.Text))
      {
        sspError.SetError(tbxFomodFileName, "FOMod File Name is required.");
        return false;
      }
      return true;
    }

    /// <summary>
    ///   Ensures that the user has selected files to include in the FOMod.
    /// </summary>
    /// <returns>
    ///   <lang langref="true" /> if the user has selected files to include in the FOMod;
    ///   <lang langref="false" /> otherwise.
    /// </returns>
    protected bool ValidateFomodFiles()
    {
      sspError.SetStatus(ffsFileStructure, null);
      if (ffsFileStructure.FindFomodFiles("*").Count == 0)
      {
        sspError.SetStatus(ffsFileStructure, "You must select files to include in the FOMod.");
        return false;
      }
      return true;
    }

    /// <summary>
    ///   Handles the <see cref="Control.Validating" /> event of the file name textbox.
    /// </summary>
    /// <remarks>
    ///   This validates the file name.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">A <see cref="CancelEventArgs" /> describing the event arguments.</param>
    private void tbxFomodFileName_Validating(object sender, CancelEventArgs e)
    {
      ValidateFomodFileName();
    }

    #endregion

    #region Save Locations Tab

    /// <summary>
    ///   Ensures that the user has entered a Premade FOMod Pack save path, if a PFP is being created.
    /// </summary>
    /// <returns>
    ///   <lang langref="true" /> if the user has entered a path and a Premade FOMod Pack is being created;
    ///   <lang langref="false" /> otherwise.
    /// </returns>
    protected bool ValidatePFPSavePath()
    {
      sspError.SetError(cbxPFP, null);
      if (String.IsNullOrEmpty(tbxPFPPath.Text) && cbxPFP.Checked)
      {
        sspError.SetError(cbxPFP, "Premade FOMod Pack save location is required.");
        return false;
      }
      return true;
    }

    /// <summary>
    ///   Handles the <see cref="Control.Validating" /> event of the Premade FOMod Pack save path textbox.
    /// </summary>
    /// <remarks>
    ///   This validates the Premade FOMod Pack save path.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">A <see cref="CancelEventArgs" /> describing the event arguments.</param>
    private void tbxPFPPath_Validating(object sender, CancelEventArgs e)
    {
      ValidatePFPSavePath();
    }

    #endregion

    #endregion

    #region Download Locations

    /// <summary>
    ///   Populates the source download location list with the selected sources.
    /// </summary>
    protected void UpdateDownloadLocationsList()
    {
      var lstOldLocations = sdsDownloadLocations.DataSource;
      var lstLocations = new List<SourceFile>();
      var strSources = ffsFileStructure.Sources;
      var lklSourceFiles = new LinkedList<string>();
      foreach (var strSource in strSources)
      {
        lklSourceFiles.Clear();
        if (Archive.IsArchive(strSource))
        {
          var strVolumes = new Archive(strSource).VolumeFileNames;
          foreach (var strVolumneName in strVolumes)
          {
            lklSourceFiles.AddLast(strVolumneName);
          }
        }
        else
        {
          lklSourceFiles.AddLast(strSource);
        }

        foreach (var strSourceFile in lklSourceFiles)
        {
          var booFound = false;
          for (var i = lstOldLocations.Count - 1; i >= 0; i--)
          {
            if (lstOldLocations[i].Source.Equals(strSourceFile))
            {
              lstLocations.Add(lstOldLocations[i]);
              lstOldLocations.RemoveAt(i);
              booFound = true;
              break;
            }
          }
          if (!booFound)
          {
            foreach (var sflLocation in lstLocations)
            {
              if (sflLocation.Source.Equals(strSourceFile))
              {
                booFound = true;
                break;
              }
            }
          }
          if (!booFound)
          {
            lstLocations.Add(new SourceFile(strSourceFile, null, false, !strSource.Equals(strSourceFile), false));
          }
        }
      }
      //make sure all the hidden sources are in the download list
      foreach (var sflOldSource in lstOldLocations)
      {
        if (sflOldSource.Hidden && !lstLocations.Contains(sflOldSource))
        {
          lstLocations.Add(sflOldSource);
        }
      }
      sdsDownloadLocations.DataSource = lstLocations;
    }

    #endregion

    #region Readme

    /// <summary>
    ///   Handles the <see cref="Control.Click" /> event of the generate readme button.
    /// </summary>
    /// <remarks>
    ///   This display the <see cref="ReadmeGeneratorForm" />, then selects the approriate readme
    ///   editor and sets its text, based on the form's output.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">An <see cref="EventArgs" /> describing the event arguments.</param>
    private void butGenerateReadme_Click(object sender, EventArgs e)
    {
      m_rgdGenerator.Sources = ffsFileStructure.Sources;
      if (m_rgdGenerator.ShowDialog(this) == DialogResult.OK)
      {
        redReadmeEditor.Readme = new Readme(m_rgdGenerator.Format, m_rgdGenerator.GeneratedReadme);
      }
    }

    /// <summary>
    ///   If no readme has been entered, this method looks for a readme file in the selected
    ///   files, and, if one is found, uses it to populate the readme editor.
    /// </summary>
    protected void SetReadmeDefault()
    {
      if (redReadmeEditor.Readme == null)
      {
        List<KeyValuePair<string, string>> lstReadmes = null;
        foreach (var strExtension in Readme.ValidExtensions)
        {
          lstReadmes = ffsFileStructure.FindFomodFiles("readme - " + tbxFomodFileName.Text + strExtension);
          if (lstReadmes.Count > 0)
          {
            break;
          }
        }
        if (lstReadmes.Count == 0)
        {
          foreach (var strExtension in Readme.ValidExtensions)
          {
            lstReadmes = ffsFileStructure.FindFomodFiles("*readme*" + strExtension);
            if (lstReadmes.Count > 0)
            {
              break;
            }
          }
        }
        if (lstReadmes.Count == 0)
        {
          foreach (var strExtension in Readme.ValidExtensions)
          {
            lstReadmes = ffsFileStructure.FindFomodFiles("*" + strExtension);
            if (lstReadmes.Count > 0)
            {
              break;
            }
          }
        }

        Readme rmeReadme = null;
        foreach (var kvpReadme in lstReadmes)
        {
          if (Readme.IsValidReadme(kvpReadme.Key))
          {
            string strReadme = null;
            if (kvpReadme.Value.StartsWith(Archive.ARCHIVE_PREFIX))
            {
              var kvpArchiveInfo = Archive.ParseArchivePath(kvpReadme.Value);
              var arcArchive = new Archive(kvpArchiveInfo.Key);
              strReadme = TextUtil.ByteToString(arcArchive.GetFileContents(kvpArchiveInfo.Value));
            }
            else if (File.Exists(kvpReadme.Value))
            {
              strReadme = File.ReadAllText(kvpReadme.Value);
            }
            rmeReadme = new Readme(kvpReadme.Key, strReadme);
            break;
          }
        }
        redReadmeEditor.Readme = rmeReadme ?? new Readme(ReadmeFormat.PlainText, null);
      }
    }

    #endregion

    #region Script

    /// <summary>
    ///   Handles the <see cref="CheckBox.CheckChanged" /> event of the use script check box.
    /// </summary>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">An <see cref="EventArgs" /> describing the event arguments.</param>
    private void cbxUseScript_CheckedChanged(object sender, EventArgs e)
    {
      if (cbxUseScript.Checked)
      {
        SetScriptDefault();
      }
    }

    /// <summary>
    ///   If no script has been entered, this method looks for a script file in the selected
    ///   files, and, if one is found, uses it to populate the script editor. If one is not found,
    ///   the script is populated with the default value.
    /// </summary>
    protected void SetScriptDefault()
    {
      if (fseScriptEditor.Script == null)
      {
        FomodScript fscInstallScript = null;
        string strScriptPath = null;
        foreach (var strScriptName in FomodScript.ScriptNames)
        {
          strScriptPath = Path.Combine("fomod", strScriptName);
          IList<KeyValuePair<string, string>> lstFiles = ffsFileStructure.FindFomodFiles(strScriptPath);
          if (lstFiles.Count > 0)
          {
            fscInstallScript = new FomodScript(strScriptName, null);
            strScriptPath = lstFiles[0].Value;
            break;
          }
        }

        if (fscInstallScript == null)
        {
          if (cbxUseScript.Checked)
          {
            fscInstallScript = new FomodScript(FomodScriptType.CSharp, Program.GameMode.DefaultCSharpScript);
          }
        }
        else
        {
          cbxUseScript.Checked = true;
          if (strScriptPath.StartsWith(Archive.ARCHIVE_PREFIX))
          {
            var kvpArchiveInfo = Archive.ParseArchivePath(strScriptPath);
            var arcArchive = new Archive(kvpArchiveInfo.Key);
            fscInstallScript.Text = TextUtil.ByteToString(arcArchive.GetFileContents(kvpArchiveInfo.Value));
          }
          else if (File.Exists(strScriptPath))
          {
            fscInstallScript.Text = File.ReadAllText(strScriptPath);
          }
        }

        fseScriptEditor.Script = fscInstallScript;
      }
    }

    #endregion

    #region Info

    /// <summary>
    ///   If no info has been entered, this method looks for an info file in the selected
    ///   files, and, if one is found, uses it to populate the info editor. If one is not found,
    ///   the editor is populated with default values.
    /// </summary>
    protected void SetInfoDefault()
    {
      if (!m_booLoadedInfo)
      {
        m_booLoadedInfo = true;
        var strInfoFileName = "fomod" + Path.DirectorySeparatorChar + "info.xml";
        IList<KeyValuePair<string, string>> lstFiles = ffsFileStructure.FindFomodFiles(strInfoFileName);
        if (lstFiles.Count > 0)
        {
          var xmlInfo = new XmlDocument();
          var kvpScript = lstFiles[0];
          if (kvpScript.Value.StartsWith(Archive.ARCHIVE_PREFIX))
          {
            var kvpArchiveInfo = Archive.ParseArchivePath(kvpScript.Value);
            var arcArchive = new Archive(kvpArchiveInfo.Key);
            var strInfo = TextUtil.ByteToString(arcArchive.GetFileContents(kvpArchiveInfo.Value));
            xmlInfo.LoadXml(strInfo);
          }
          else if (File.Exists(kvpScript.Value))
          {
            xmlInfo.Load(kvpScript.Value);
          }

          fomod.LoadInfo(xmlInfo, finInfo, false);
        }
        else if (String.IsNullOrEmpty(finInfo.ModName))
        {
          finInfo.ModName = tbxFomodFileName.Text;
        }

        var strScreenshotFileName = "fomod" + Path.DirectorySeparatorChar + "screenshot.*";
        IList<KeyValuePair<string, string>> lstScreenshotFiles = ffsFileStructure.FindFomodFiles(strScreenshotFileName);
        if (lstScreenshotFiles.Count > 0)
        {
          var kvpScreenshot = lstScreenshotFiles[0];
          if (kvpScreenshot.Value.StartsWith(Archive.ARCHIVE_PREFIX))
          {
            var kvpArchiveInfo = Archive.ParseArchivePath(kvpScreenshot.Value);
            var arcArchive = new Archive(kvpArchiveInfo.Key);
            var bteScreenshot = arcArchive.GetFileContents(kvpArchiveInfo.Value);
            finInfo.Screenshot = new Screenshot(kvpArchiveInfo.Value, bteScreenshot);
          }
          else if (File.Exists(kvpScreenshot.Value))
          {
            finInfo.Screenshot = new Screenshot(kvpScreenshot.Value, File.ReadAllBytes(kvpScreenshot.Value));
          }
        }
      }
    }

    #endregion

    /// <summary>
    ///   Handles the <see cref="FomodScriptEditor.GotXMLAutoCompleteList" /> event of the script
    ///   editor.
    /// </summary>
    /// <remarks>
    ///   This methods populates the code completion list with the file paths in the FOMod file structure
    ///   when the value being completed is the source value of a file tag.
    /// </remarks>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">A <see cref="RegeneratableAutoCompleteListEventArgs" /> decribing the event arguments.</param>
    private void fseScriptEditor_GotXMLAutoCompleteList(object sender, RegeneratableAutoCompleteListEventArgs e)
    {
      if (!String.IsNullOrEmpty(e.ElementPath) &&
          (Path.GetFileName(e.ElementPath).Equals("file") || Path.GetFileName(e.ElementPath).Equals("folder")) &&
          (e.AutoCompleteType == AutoCompleteType.AttributeValues) &&
          (e.Siblings[e.Siblings.Length - 1].Equals("source")))
      {
        var strPrefix = e.LastWord.EndsWith("=") ? "" : e.LastWord;
        var lstFiles = ffsFileStructure.FindFomodFiles(strPrefix + "*");
        foreach (var kvpFile in lstFiles)
        {
          e.AutoCompleteList.Add(new XmlCompletionData(AutoCompleteType.AttributeValues, kvpFile.Key, null));
        }
        e.GenerateOnNextKey = true;
        e.ExtraInsertionCharacters.Add(Path.DirectorySeparatorChar);
      }
    }

    /// <summary>
    ///   Handles the <see cref="Control.Click" /> event of the select PFP folder button.
    /// </summary>
    /// <param name="sender">The object that raised the event.</param>
    /// <param name="e">An <see cref="EventArgs" /> describing the event arguments.</param>
    private void butSelectPFPFolder_Click(object sender, EventArgs e)
    {
      fbdPFPPath.SelectedPath = tbxPFPPath.Text;
      if (fbdPFPPath.ShowDialog(this) == DialogResult.OK)
      {
        tbxPFPPath.Text = fbdPFPPath.SelectedPath;
      }
    }
  }
}