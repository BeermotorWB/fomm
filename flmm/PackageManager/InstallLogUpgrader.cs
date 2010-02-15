﻿using System;
using System.IO;
using System.Xml;
using ICSharpCode.SharpZipLib.Checksums;
using ChinhDo.Transactions;
using System.Transactions;
using System.Collections.Generic;

namespace Fomm.PackageManager
{
	class InstallLogUpgrader : InstallLog
	{
		private static object m_objLock = new object();
		private TxFileManager m_tfmFileManager = null;
		private XmlDocument m_xmlOldInstallLog = null;

		internal InstallLogUpgrader()
			: base()
		{
		}

		public void UpgradeInstallLog()
		{
			//this is to handle the few people who already installed a version that used
			// the new-style install log, but before it had a version
			if (Document.SelectNodes("descendant::installingMods").Count > 0)
			{
				SetInstallLogVersion(new Version("0.1.0.0"));
				Save();
				return;
			}

			//we only want one upgrade at a time happening to minimize the chances of
			// messed up install logs.
			lock (m_objLock)
			{
				EnableLogFileRefresh = false;
				using (TransactionScope tsTransaction = new TransactionScope())
				{
					string[] strModInstallFiles = Directory.GetFiles(Program.PackageDir, "*.XMl", SearchOption.TopDirectoryOnly);
					XmlDocument xmlModInstallLog = null;
					string strModBaseName = null;

					m_tfmFileManager = new TxFileManager();
					m_tfmFileManager.Snapshot(InstallLogPath);
					m_xmlOldInstallLog = new XmlDocument();
					m_xmlOldInstallLog.Load(InstallLogPath);
					Reset();

					foreach (string strModInstallLog in strModInstallFiles)
					{
						strModBaseName = Path.GetFileNameWithoutExtension(strModInstallLog);
						xmlModInstallLog = new XmlDocument();
						xmlModInstallLog.Load(strModInstallLog);

						UpgradeInstalledFiles(xmlModInstallLog, strModInstallLog, strModBaseName);
						UpgradeIniEdits(xmlModInstallLog, strModBaseName);
						UpgradeSdpEdits(xmlModInstallLog, strModBaseName);
						m_tfmFileManager.Move(strModInstallLog, strModInstallLog + ".bak");
					}
					SetInstallLogVersion(new Version("0.1.0.0"));
					Save();
					tsTransaction.Complete();
					m_tfmFileManager = null;
				}
			}
		}

		#region Sdp Edits Upgrade

		private byte[] GetOldSdpValue(Int32 p_intPackage, string p_strShader)
		{
			XmlNode node = m_xmlOldInstallLog.SelectSingleNode("descendant::sdp[@package='" + p_intPackage + "' and @shader='" + p_strShader + "']");
			if (node == null)
				return null;
			byte[] b=new byte[node.InnerText.Length/2];
            for(int i=0;i<b.Length;i++) {
                b[i]=byte.Parse(""+node.InnerText[i*2]+node.InnerText[i*2+1], System.Globalization.NumberStyles.AllowHexSpecifier);
            }
			return b;
		}

		private List<string> m_lstSeenShader = new List<string>();
		private void UpgradeSdpEdits(XmlDocument p_xmlModInstallLog, string p_strModBaseName)
		{
			XmlNodeList xnlIniEdits = p_xmlModInstallLog.SelectNodes("descendant::sdpEdits/*");
			foreach (XmlNode xndIniEdit in xnlIniEdits)
			{
				Int32 intPackage = Int32.Parse(xndIniEdit.Attributes.GetNamedItem("package").Value);
				string strShader = xndIniEdit.Attributes.GetNamedItem("shader").Value;
				byte[] bteOldValue = GetOldSdpValue(intPackage, strShader);
				//we have no way of knowing who last edited the shader - that information
				// was not tracked
				// so, let's just do first come first serve 
				if (!m_lstSeenShader.Contains(intPackage + "~" + strShader.ToLowerInvariant()))
				{
					//this is the first mod we have encountered that edited this shader,
					// so let's assume it is the lastest mod to have made the edit...
					AddShaderEdit(p_strModBaseName, intPackage, strShader, SDPArchives.GetShader(intPackage, strShader));
					//...and backup the old value as the original value
					PrependAfterOriginalShaderEdit(ORIGINAL_VALUES, intPackage, strShader, bteOldValue);
					m_lstSeenShader.Add(intPackage + "~" + strShader.ToLowerInvariant());
				}
				else
				{
					//someone else made the shader edit
					// we don't know what value was overwritten, so we will just use what we have
					// which is the old value
					PrependAfterOriginalShaderEdit(p_strModBaseName, intPackage, strShader, bteOldValue);
				}
			}
		}

		/// <summary>
		/// Adds a node representing that the specified mod made the specified sdp edit.
		/// </summary>
		/// <remarks>
		/// This method prepends the node to the beginning of the list of installing mods, but
		/// after the ORIGINAL_VALUES node if it exists, indicating that the specified mod is not
		/// the latest mod to edit the specified shader.
		/// </remarks>
		/// <param name="p_strModName">The base name of the mod that made the edit.</param>
		/// <param name="p_intPackage">The package containing the shader that was edited.</param>
		/// <param name="p_strShaderName">The shader that was edited.</param>
		/// <param name="p_bteData">The value to which to the shader was set.</param>
		protected void PrependAfterOriginalShaderEdit(string p_strModName, int p_intPackage, string p_strShader, byte[] p_bteData)
		{
			XmlNode xndModList = null;
			XmlNode xndInstallingMod = CreateSdpEditNode(GetModKey(p_strModName), p_intPackage, p_strShader, p_bteData, out xndModList);
			if ((xndModList.FirstChild != null) && (xndModList.FirstChild.Attributes["key"].InnerText.Equals(OriginalValuesKey)))
				xndModList.InsertAfter(xndInstallingMod, xndModList.FirstChild);
			else
				xndModList.PrependChild(xndInstallingMod);
		}

		#endregion

		#region Ini Edits Upgrade

		private string GetOldIniValue(string p_strFile, string p_strSection, string p_strKey, out string p_strModName)
		{
			p_strModName = null;
			XmlNode node = m_xmlOldInstallLog.SelectSingleNode("descendant::ini[@file='" + p_strFile + "' and @section='" + p_strSection + "' and @key='" + p_strKey + "']");
			if (node == null)
				return null;
			XmlNode modnode = node.Attributes.GetNamedItem("mod");
			if (modnode != null)
				p_strModName = modnode.Value;
			return node.InnerText;
		}

		private void UpgradeIniEdits(XmlDocument p_xmlModInstallLog, string p_strModBaseName)
		{
			XmlNodeList xnlIniEdits = p_xmlModInstallLog.SelectNodes("descendant::iniEdits/*");
			foreach (XmlNode xndIniEdit in xnlIniEdits)
			{
				string strFile = xndIniEdit.Attributes.GetNamedItem("file").Value;
				string strSection = xndIniEdit.Attributes.GetNamedItem("section").Value;
				string strKey = xndIniEdit.Attributes.GetNamedItem("key").Value;
				string strOldIniEditor = null;
				string strOldValue = GetOldIniValue(strFile, strSection, strKey, out strOldIniEditor);
				if (p_strModBaseName.Equals(strOldIniEditor))
				{
					//this mod owns the ini edit, so append it to the list of editing mods...
					AddIniEdit(strFile, strSection, strKey, p_strModBaseName, NativeMethods.GetPrivateProfileString(strSection, strKey, "", strFile));
					//...and backup the old value as the original value
					PrependAfterOriginalIniEdit(strFile, strSection, strKey, ORIGINAL_VALUES, strOldValue);
				}
				else
				{
					//someone else made the ini edit
					// we don't know what value was overwritten, so we will just use what we have
					// which is the old value stored in the old install log
					PrependAfterOriginalIniEdit(strFile, strSection, strKey, p_strModBaseName, strOldValue);
				}
			}
		}

		/// <summary>
		/// Adds a node representing that the specified mod made the specified Ini edit.
		/// </summary>
		/// <remarks>
		/// This method prepends the node to the beginning of the list of installing mods, but
		/// after the ORIGINAL_VALUES node if it exists, indicating that the specified mod is not
		/// the latest mod to edit the specified Ini value.
		/// </remarks>
		/// <param name="p_strFile">The Ini file that was edited.</param>
		/// <param name="p_strSection">The section in the Ini file that was edited.</param>
		/// <param name="p_strKey">The key in the Ini file that was edited.</param>
		/// <param name="p_strModName">The base name of the mod that made the edit.</param>
		/// <param name="p_strValue">The value to which to the key was set.</param>
		protected void PrependAfterOriginalIniEdit(string p_strFile, string p_strSection, string p_strKey, string p_strModName, string p_strValue)
		{
			XmlNode xndModList = null;
			XmlNode xndInstallingMod = CreateIniEditNode(GetModKey(p_strModName), p_strFile, p_strSection, p_strKey, p_strValue, out xndModList);
			if ((xndModList.FirstChild != null) && (xndModList.FirstChild.Attributes["key"].InnerText.Equals(OriginalValuesKey)))
				xndModList.InsertAfter(xndInstallingMod, xndModList.FirstChild);
			else
				xndModList.PrependChild(xndInstallingMod);
		}

		#endregion

		#region Installed Files Upgrade

		private void UpgradeInstalledFiles(XmlDocument p_xmlModInstallLog, string p_strModInstallLogPath, string p_strModBaseName)
		{
			Int32 intDataPathStartPos = Path.GetFullPath("data").Length + 1;
			XmlNodeList xnlFiles = p_xmlModInstallLog.SelectNodes("descendant::installedFiles/*");
			foreach (XmlNode xndFile in xnlFiles)
			{
				AddMod(p_strModBaseName);
				string strFile = xndFile.InnerText;
				if (!File.Exists(strFile))
					continue;
				fomod fomodMod = new fomod(p_strModInstallLogPath.ToLowerInvariant().Replace(".xml", ".fomod"));
				string strDataRelativePath = strFile.Substring(intDataPathStartPos);

				Crc32 crcDiskFile = new Crc32();
				Crc32 crcFomodFile = new Crc32();
				crcDiskFile.Update(File.ReadAllBytes(strFile));
				byte[] bteFomodFile = fomodMod.GetFile(strDataRelativePath);
				crcFomodFile.Update(bteFomodFile);
				if (!crcDiskFile.Value.Equals(crcFomodFile.Value))
				{
					//another mod owns the file, so put this mod's file into
					// the overwrites directory
					string strDirectory = Path.GetDirectoryName(strDataRelativePath);
					string strBackupPath = Path.GetFullPath(Path.Combine(Program.overwriteDir, strDirectory));
					string strModKey = GetModKey(p_strModBaseName);
					if (!Directory.Exists(strBackupPath))
						m_tfmFileManager.CreateDirectory(strBackupPath);
					strBackupPath = Path.Combine(strBackupPath, strModKey + "_" + Path.GetFileName(strDataRelativePath));
					m_tfmFileManager.WriteAllBytes(strBackupPath, bteFomodFile);
					PrependDataFile(p_strModBaseName, strDataRelativePath);
				}
				else
				{
					//this mod owns the file, so append it to the list of installing mods
					AddDataFile(p_strModBaseName, strDataRelativePath);
				}
			}
		}

		/// <summary>
		/// Adds a node representing that the specified mod installed the specified file.
		/// </summary>
		/// <remarks>
		/// This method prepends the node to the beginning of the list of installing mods, indicating
		/// that the specified mod is not the latest mod to install the specified file.
		/// </remarks>
		/// <param name="p_strModName">The base name of the mod that installed the file.</param>
		/// <param name="p_strPath">The path of the file that was installed.</param>
		protected void PrependDataFile(string p_strModName, string p_strPath)
		{
			XmlNode xndModList = null;
			XmlNode xndInstallingMod = CreateDataFileNode(GetModKey(p_strModName), p_strPath, out xndModList);
			xndModList.PrependChild(xndInstallingMod);
		}

		#endregion
	}
}