using System;

namespace OpenDesk.Core.Models
{
    public enum WorkspaceSource { Local, GoogleDrive }

    public class WorkspaceEntry
    {
        public string          Name         { get; set; } = "";
        public string          FullPath     { get; set; } = "";
        public WorkspaceSource Source       { get; set; }
        public DateTime        LastModified { get; set; }
        public bool            IsDirectory  { get; set; }
        public long            SizeBytes    { get; set; }

        // Drive 전용
        public string DriveFileId { get; set; } = "";
        public string MimeType    { get; set; } = "";
    }
}
