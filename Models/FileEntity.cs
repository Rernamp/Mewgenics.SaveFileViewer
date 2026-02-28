using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Mewgenics.SaveFileViewer.Models {
    [Table("files")]
    public class FileEntity {
        [Key]
        [Column("key")]
        public string Key { get; set; } = string.Empty;

        [Column("data")]
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}