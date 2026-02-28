using System.Text;

namespace Mewgenics.SaveFileViewer.Models {
    public class CatEntity {
        public int Key { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
    }

    public class ParsedCat {
        public int Key { get; set; }
        public ulong Id64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Sex { get; set; } = "Unknown";
        public int NameEndRaw { get; set; }
        public CatFlags Flags { get; set; } = new();
        public CatStats? Stats { get; set; }
        public CatStats? LevelBonuses { get; set; }
        public int? StatsOffset { get; set; }
        public CombatState? Combat { get; set; }
        public string? ClassName { get; set; }
        public int? BirthdayDay { get; set; }
        public int? BirthdayOffset { get; set; }
        public MutationResult Mutations { get; set; } = new();
        public List<EquipSlot> Equipment { get; set; } = new();
        public List<AbilitySlot> Abilities { get; set; } = new();
        public byte[] DecompressedBlob { get; set; } = Array.Empty<byte>();
        public string Lz4Variant { get; set; } = "standard";

        public string DisplayName => $"{Name} (Key: {Key})";
    }

    public class CatFlags {
        public int Raw { get; set; }
        public int Offset { get; set; }
        public bool Retired { get; set; }
        public bool Dead { get; set; }
        public bool Donated { get; set; }
    }

    public class CatStats {
        public int Str { get; set; }
        public int Dex { get; set; }
        public int Con { get; set; }
        public int Int { get; set; }
        public int Spd { get; set; }
        public int Cha { get; set; }
        public int Luck { get; set; }
    }

    public class CombatState {
        public string StatusEffect { get; set; } = string.Empty;
        public int Hp { get; set; }
        public int HpOffset { get; set; }
        public int StatusOffset { get; set; }
    }

    public class MutationResult {
        public int? BaseOffset { get; set; }
        public int? CoatId { get; set; }
        public int? CoatOffset { get; set; }
        public List<MutationSlot> Slots { get; set; } = new();
    }

    public class MutationSlot {
        public int SlotIndex { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public int SlotId { get; set; }
        public int Offset { get; set; }
    }

    public class EquipSlot {
        public int BlobSlot { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public string? ItemId { get; set; }
        public bool ImplicitEmpty { get; set; }
    }

    public class AbilitySlot {
        public string Label { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string AbilityId { get; set; } = string.Empty;
        public int? Tier { get; set; }
        public int? RunStart { get; set; }
        public int? RunEnd { get; set; }
        public int? RunIndex { get; set; }
        public int? RecordOffset { get; set; }
        public int? ByteLength { get; set; }
    }

    public class U64Str {
        public int Offset { get; set; }
        public int ByteLength { get; set; }
        public string Value { get; set; } = string.Empty;
    }

    public class U64StrTier : U64Str {
        public int Tier { get; set; }
    }

    public static class MutationSlotInfo {
        public static readonly Dictionary<int, (string Label, string Category)> SlotInfo = new()
        {
            { 1, ("Eyes", "Appearance") },
            { 2, ("Ears", "Appearance") },
            { 3, ("Tail", "Appearance") },
            { 4, ("Body", "Appearance") },
            { 5, ("Pattern", "Appearance") },
            { 6, ("Color 1", "Appearance") },
            { 7, ("Color 2", "Appearance") },
            { 8, ("Color 3", "Appearance") },
            { 9, ("Mutation 1", "Mutation") },
            { 10, ("Mutation 2", "Mutation") },
            { 11, ("Mutation 3", "Mutation") },
            { 12, ("Mutation 4", "Mutation") },
            { 13, ("Mutation 5", "Mutation") },
            { 14, ("Mutation 6", "Mutation") }
        };

        public static (string Label, string Category) GetInfo(int slotIndex) {
            return SlotInfo.TryGetValue(slotIndex, out var info) ? info : ($"Slot {slotIndex}", "Unknown");
        }
    }
}