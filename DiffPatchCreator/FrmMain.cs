﻿using Ionic.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DiffPatchCreator
{
	public partial class FrmMain : Form
	{
		private const string XDeltaFileName = "xdelta3.exe";
		private const string PatchFileExtension = ".patch";
		private const string TempDirName = "tmp_patch";
		private readonly string TempFilesPath = Path.Combine(TempDirName, "files");
		private readonly string ChangesPath = Path.Combine(TempDirName, "changes.txt");

		public FrmMain()
		{
			InitializeComponent();
		}

		private void ShowError(string message)
		{
			MessageBox.Show(message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void ShowInfo(string message)
		{
			MessageBox.Show(message, this.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
		}

		private Dictionary<string, FileInfo> GetRelativeFileInformation(string path)
		{
			var result = new Dictionary<string, FileInfo>();

			foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
			{
				var relativePath = file.Replace(path, "").TrimStart('\\', '/');
				result[relativePath] = new FileInfo(file);
			}

			return result;
		}

		private string CreatePatchList(IEnumerable<string> added, IEnumerable<string> removed, IEnumerable<string> changed)
		{
			var sb = new StringBuilder();

			foreach (var file in added)
				sb.AppendLine("+" + file);

			foreach (var file in removed)
				sb.AppendLine("-" + file);

			foreach (var file in changed)
				sb.AppendLine("*" + file);

			var patchList = sb.ToString();

			File.WriteAllText(ChangesPath, patchList);

			return patchList;
		}

		private void PrepareTempFolder()
		{
			this.RemoveTempFolder();
			Directory.CreateDirectory(TempDirName);
		}

		private void RemoveTempFolder()
		{
			if (Directory.Exists(TempDirName))
				Directory.Delete(TempDirName, true);
		}

		private void CopyAddedFiles(string newPath, IEnumerable<string> added)
		{
			foreach (var file in added)
			{
				var newFilePath = Path.Combine(newPath, file);
				var patchFilePath = Path.Combine(TempFilesPath, file);
				var patchFileFolder = Path.GetDirectoryName(patchFilePath);

				if (!Directory.Exists(patchFileFolder))
				{
					Directory.CreateDirectory(patchFileFolder);
					Thread.Sleep(100);
				}

				File.Copy(newFilePath, patchFilePath);
			}
		}

		private void CreateDiffs(string oldPath, string newPath, IEnumerable<string> changed)
		{
			foreach (var file in changed)
			{
				var oldFilePath = Path.Combine(oldPath, file);
				var newFilePath = Path.Combine(newPath, file);
				var patchFilePath = Path.Combine(TempFilesPath, file + PatchFileExtension);
				var patchFileDirPath = Path.GetDirectoryName(patchFilePath);

				if (!Directory.Exists(patchFileDirPath))
					Directory.CreateDirectory(patchFileDirPath);

				var process = new Process();
				process.StartInfo.FileName = XDeltaFileName;
				process.StartInfo.Arguments = string.Format("-v -A -e -0 -f -s {0} {1} {2}", oldFilePath, newFilePath, patchFilePath);
				process.StartInfo.UseShellExecute = false;
				process.StartInfo.CreateNoWindow = true;
				process.StartInfo.RedirectStandardOutput = true;
				process.StartInfo.RedirectStandardError = true;
				process.Start();
				process.WaitForExit();

				if (process.ExitCode != 0)
				{
					var stdout = process.StandardOutput.ReadToEnd();
					var stderr = process.StandardError.ReadToEnd();

					throw new Exception("Failed to create patch for '" + file + "', xdelta error: " + stderr);
				}
			}
		}

		private void CreateArchive(string patchFileName)
		{
			var patchDirPath = Path.GetDirectoryName(patchFileName);
			if (!Directory.Exists(patchFileName))
				Directory.CreateDirectory(patchDirPath);

			using (var zip = new ZipFile())
			{
				zip.AddDirectory(TempDirName, "");
				zip.Save(patchFileName);
			}
		}

		private void BtnCreate_Click(object sender, EventArgs e)
		{
			this.BtnCreate.Enabled = false;
			this.BtnCreate.Text = "Working...";
			this.Refresh();

			try
			{
				var oldPath = this.TxtOldPath.Text;
				var newPath = this.TxtNewPath.Text;
				var patchFileName = this.TxtPatchFileName.Text;

				if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
				{
					this.ShowError("Please enter a path for both the new and the old version.");
					return;
				}

				if (!Directory.Exists(oldPath))
				{
					this.ShowError("Old version directoy doesn't exist.");
					return;
				}

				if (!Directory.Exists(newPath))
				{
					this.ShowError("New version directoy doesn't exist.");
					return;
				}

				if (string.IsNullOrWhiteSpace(patchFileName))
				{
					this.ShowError("Please enter patch file name.");
					return;
				}

				if (!File.Exists(XDeltaFileName))
				{
					this.ShowError("File not found: " + XDeltaFileName);
					return;
				}

				var oldFiles = this.GetRelativeFileInformation(oldPath);
				var newFiles = this.GetRelativeFileInformation(newPath);

				var added = newFiles.Where(a => !oldFiles.ContainsKey(a.Key)).Select(a => a.Key);
				var removed = oldFiles.Where(a => !newFiles.ContainsKey(a.Key)).Select(a => a.Key);
				var changed = newFiles.Where(a => oldFiles.ContainsKey(a.Key) && oldFiles[a.Key].LastWriteTime != newFiles[a.Key].LastWriteTime).Select(a => a.Key);

				this.PrepareTempFolder();
				this.CreatePatchList(added, removed, changed);
				this.CopyAddedFiles(newPath, added);
				this.CreateDiffs(oldPath, newPath, changed);
				this.CreateArchive(patchFileName);
				this.RemoveTempFolder();

				this.ShowInfo(string.Format("Done!\n\nAdded: {0}\nRemoved: {1}\nChanged: {2}", added.Count(), removed.Count(), changed.Count()));
			}
			catch (Exception ex)
			{
				this.ShowError("Error: " + ex.ToString());
			}

			this.BtnCreate.Enabled = true;
			this.BtnCreate.Text = "Create";
		}

		private void BtnBrowseOld_Click(object sender, EventArgs e)
		{
			if (this.FolderBrowser.ShowDialog() != DialogResult.OK)
				return;

			this.TxtOldPath.Text = this.FolderBrowser.SelectedPath;
		}

		private void BtnBrowseNew_Click(object sender, EventArgs e)
		{
			if (this.FolderBrowser.ShowDialog() != DialogResult.OK)
				return;

			this.TxtNewPath.Text = this.FolderBrowser.SelectedPath;
		}

		private void BtnBrowsePatchFilename_Click(object sender, EventArgs e)
		{
			if (this.SaveFile.ShowDialog() != DialogResult.OK)
				return;

			var fileName = this.SaveFile.FileName;
			var currentDir = Directory.GetCurrentDirectory();
			if (fileName.StartsWith(currentDir))
				fileName = fileName.Replace(currentDir, "").TrimStart('\\', '/');

			this.TxtPatchFileName.Text = fileName;
		}
	}
}
