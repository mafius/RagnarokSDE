using System;
using System.IO;
using ErrorManager;
using GRF.IO;
using SDE.Tools.DatabaseEditor.Engines;
using SDE.Tools.DatabaseEditor.Engines.Parsers;
using SDE.Tools.DatabaseEditor.Generic.Lists;
using Utilities.Extension;

namespace SDE.Tools.DatabaseEditor.Generic.DbLoaders {
	public class DbDebugItem<TKey> {
		private readonly AbstractDb<TKey> _db;

		public DbDebugItem(AbstractDb<TKey> db) {
			_db = db;
			DbSource = _db.DbSource;
			NumberOfErrors = 3;
			AllLoaders.LatestFile = null;
			TextFileHelper.LastReader = null;
		}

		public AbstractDb<TKey> AbsractDb {
			get { return _db; }
		}
		public int NumberOfErrors { get; set; }
		public string FilePath { get; set; }
		public string OldPath { get; set; }
		public FileType FileType { get; set; }
		public string SubPath { get; set; }
		public ServerDBs DbSource { get; set; }
		public bool IsRenewal { get; set; }

		public bool Load(ServerDBs dbSource) {
			DbSource = dbSource;
			string path = AllLoaders.DetectPath(DbSource);

			AllLoaders.LatestFile = path;

			if (String.IsNullOrEmpty(path)) {
				if (_db.ThrowFileNotFoundException) {
					DbLoaderErrorHandler.Handle("File not found '" + DbSource + "'.", ErrorLevel.NotSpecified);
				}

				return false;
			}

			FileType = AllLoaders.GetFileType(path);
			FilePath = path;
			AllLoaders.BackupFile(FilePath);
			return true;
		}

		public bool Load() {
			return Load(DbSource);
		}

		public bool ReportException(Exception item) {
			DbLoaderErrorHandler.HandleLoader(item.Message);
			NumberOfErrors--;

			if (NumberOfErrors < -10) {
				DbLoaderErrorHandler.Handle("Failed to read too many items, the db will stop loading.", ErrorLevel.Critical);
				return false;
			}

			return true;
		}

		public void ReportIdException(string exception, object item) {
			DbLoaderErrorHandler.Handle(exception, item.ToString());
		}

		public bool ReportIdException(object item) {
			DbLoaderErrorHandler.Handle("Failed to read an item.", item.ToString());
			NumberOfErrors--;

			if (NumberOfErrors < -10) {
				DbLoaderErrorHandler.Handle("Failed to read too many items, the db [" + DbSource + "] will stop loading.", ErrorLevel.Critical);
				return false;
			}

			return true;
		}

		public bool ReportException(string item) {
			DbLoaderErrorHandler.Handle(item);
			NumberOfErrors--;

			if (NumberOfErrors < -10) {
				DbLoaderErrorHandler.Handle("Failed to read too many items, the db [" + DbSource + "] will stop loading.", ErrorLevel.Critical);
				return false;
			}

			return true;
		}

		public bool Write(string dbPath, string subPath, ServerType serverType, FileType fileType = FileType.Detect) {
			SubPath = subPath;
			string filename = DbSource.Filename;

			FileType = fileType;

			if ((fileType & FileType.Detect) == FileType.Detect) {
				if ((DbSource.SupportedFileType & FileType.Txt) == FileType.Txt) {
					FileType = FileType.Txt;
				}

				if ((DbSource.SupportedFileType & FileType.Conf) == FileType.Conf) {
					if (serverType == ServerType.Hercules) {
						FileType = FileType.Conf;
						filename = DbSource.AlternativeName ?? filename;
					}
				}

				if (FileType == FileType.Detect)
					FileType = FileType.Error;
			}

			if (FileType == FileType.Error)
				return false;

			if ((DbSource.SupportedFileType & FileType) != FileType) {
				return false;
			}

			string ext = "." + FileType.ToString().ToLower();

			IsRenewal = false;

			if (DbSource.UseSubPath) {
				if (subPath == "re")
					IsRenewal = true;

				FilePath = GrfPath.Combine(dbPath, subPath, filename + ext);
			}
			else {
				FilePath = GrfPath.Combine(dbPath, filename + ext);
			}

			AllLoaders.LatestFile = FilePath;

			string logicalPath = AllLoaders.DetectPath(DbSource);
			OldPath = AllLoaders.GetBackupFile(logicalPath);

			if (OldPath == null || !File.Exists(OldPath)) {
				return false;
			}

			if (_db.Attached["IsEnabled"] != null && !(bool)_db.Attached["IsEnabled"])
				return false;

			GrfPath.CreateDirectoryFromFile(FilePath);

			if (!_db.Table.Commands.IsModified && logicalPath.IsExtension(FilePath.GetExtension())) {
				BackupEngine.Instance.Backup(logicalPath);
				_db.DbDirectCopy(this, _db);
				return false;
			}

			BackupEngine.Instance.Backup(logicalPath);
			return true;
		}
	}
}