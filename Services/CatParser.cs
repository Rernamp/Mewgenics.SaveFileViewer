using System.Text;
using Mewgenics.SaveFileViewer.Models;
using static Mewgenics.SaveFileViewer.Services.BinaryHelpers;

namespace Mewgenics.SaveFileViewer.Services {
    public interface ICatParser {
        Task<ParsedCat> ParseCatAsync(int key, byte[] compressedData, int? currentDay = null);
        ParsedCat ReParseCatFromBlob(int key, byte[] decompressedData, string variant, int? currentDay = null);
    }

    public class CatParser : ICatParser {
        private readonly ILZ4Decompressor _lz4Decompressor;

        public CatParser(ILZ4Decompressor lz4Decompressor) {
            _lz4Decompressor = lz4Decompressor;
        }

        public async Task<ParsedCat> ParseCatAsync(int key, byte[] compressedData, int? currentDay = null) {
            var decompressed = await _lz4Decompressor.DecompressAsync(compressedData);

            if (decompressed.Length < 12)
                throw new Exception($"Cat key {key} blob too small after decompress ({decompressed.Length} bytes)");

            return ReParseCatFromBlob(key, decompressed, "standard", currentDay);
        }

        public ParsedCat ReParseCatFromBlob(int key, byte[] dec, string variant, int? currentDay = null) {
            var cat = new ParsedCat {
                Key = key,
                Id64 = ReadU64LE(dec, 4),
                DecompressedBlob = dec,
                Lz4Variant = variant
            };

            // Parse name and sex
            var nameSex = DetectNameEndAndSex(dec);
            cat.Name = nameSex.Name;
            cat.Sex = nameSex.Sex;
            cat.NameEndRaw = nameSex.NameEndRaw;

            // Parse flags
            cat.Flags = ReadStatusFlags(dec, cat.NameEndRaw);

            // Parse stats
            var statsResult = FindStats(dec);
            cat.Stats = statsResult.Stats;
            cat.LevelBonuses = statsResult.LevelBonuses;
            cat.StatsOffset = statsResult.Offset;

            // Parse combat state
            if (cat.StatsOffset.HasValue) {
                cat.Combat = ParseCombatState(dec, cat.StatsOffset.Value);
            }

            // Parse birthday info
            var birthdayInfo = FindBirthdayInfo(dec, currentDay);
            cat.ClassName = birthdayInfo.ClassName;
            cat.BirthdayDay = birthdayInfo.BirthdayDay;
            cat.BirthdayOffset = birthdayInfo.BirthdayOffset;

            // Parse mutations
            cat.Mutations = ParseMutationTable(dec);

            // Parse equipment
            cat.Equipment = ParseEquipmentSlots(dec) ?? new List<EquipSlot>();

            // Parse abilities
            cat.Abilities = BuildAbilitySlots(dec);

            return cat;
        }

        private (string Name, string Sex, int NameEndRaw) DetectNameEndAndSex(byte[] dec) {
            var bestScore = -1;
            var bestName = "";
            var bestSex = "Unknown";
            var bestNameEndRaw = 0x14;

            int[] possibleLenOffsets = { 0x0C, 0x10 };
            var sexMap = new Dictionary<int, string> { { 0, "Male" }, { 1, "Female" } };

            foreach (int offLen in possibleLenOffsets) {
                if (offLen + 4 > dec.Length) continue;

                int nameLen = (int)ReadU32LE(dec, offLen);
                if (nameLen > 128) continue;

                int start = 0x14;
                int end = start + nameLen * 2;
                if (end > dec.Length) continue;

                string name = ReadUtf16LE(dec, start, nameLen);
                string sex = "Unknown";
                int score = 0;

                int offA = end + 8;
                int offB = end + 12;
                if (offB + 2 <= dec.Length) {
                    int a = ReadU16LE(dec, offA);
                    int b = ReadU16LE(dec, offB);

                    if (a == b && sexMap.ContainsKey(a)) {
                        sex = sexMap[a];
                        score += 4;
                    } else if (sexMap.ContainsKey(a) || sexMap.ContainsKey(b)) {
                        sex = sexMap.ContainsKey(a) ? sexMap[a] :
                              (sexMap.ContainsKey(b) ? sexMap[b] : "Unknown");
                        score += 2;
                    }
                }

                if (!string.IsNullOrEmpty(name)) score += 1;

                if (score > bestScore) {
                    bestScore = score;
                    bestName = name;
                    bestSex = sex;
                    bestNameEndRaw = end;
                }
            }

            return (bestName, bestSex, bestNameEndRaw);
        }

        private CatFlags ReadStatusFlags(byte[] dec, int nameEndRaw) {
            int flagsOff = nameEndRaw + 0x10;
            var flags = new CatFlags { Offset = flagsOff };

            if (flagsOff + 2 <= dec.Length) {
                int raw = ReadU16LE(dec, flagsOff);
                flags.Raw = raw;
                flags.Retired = (raw & 0x0002) != 0;
                flags.Dead = (raw & 0x0020) != 0;
                flags.Donated = (raw & 0x4000) != 0;
            } else {
                flags.Raw = -1;
            }

            return flags;
        }

        private (CatStats? Stats, CatStats? LevelBonuses, int? Offset) FindStats(byte[] dec, int expectedOff = 0x1CC, int window = 0x140) {
            int n = dec.Length;
            if (n < 28) return (null, null, null);

            var candidates = new List<(int Off, int[] Vals, double Score)>();

            int lo = Math.Max(0, expectedOff - window);
            int hi = Math.Min(n - 28, expectedOff + window);

            for (int off = lo; off <= hi; off++) {
                bool valid = true;
                int[] vals = new int[7];

                for (int i = 0; i < 7; i++) {
                    int v = ReadI32LE(dec, off + i * 4);
                    if (v < 1 || v > 10) {
                        valid = false;
                        break;
                    }
                    vals[i] = v;
                }

                if (!valid) continue;

                int dist = Math.Abs(off - expectedOff);
                int sum = vals.Sum();
                double score = (1000 - dist) + (sum * 0.1);
                candidates.Add((off, vals, score));
            }

            if (candidates.Count == 0) return (null, null, null);

            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            var best = candidates[0];
            if (!HasCombatState(dec, best.Off)) {
                var withCombat = candidates.Find(c => HasCombatState(dec, c.Off));
                if (withCombat.Off != 0) best = withCombat;
            }

            var levelBonuses = ReadLevelBonuses(dec, best.Off + 28, n);
            var stats = ArrayToStats(best.Vals);

            return (stats, levelBonuses, best.Off);
        }

        private CatStats? ReadLevelBonuses(byte[] dec, int off, int n) {
            if (off + 28 > n) return null;
            int[] vals = new int[7];
            for (int i = 0; i < 7; i++) {
                int v = ReadI32LE(dec, off + i * 4);
                if (v < -10 || v > 50) return null;
                vals[i] = v;
            }
            return ArrayToStats(vals);
        }

        private bool HasCombatState(byte[] dec, int statsOff) {
            int csOff = statsOff + 84;
            if (csOff + 13 > dec.Length) return false;

            uint statusLen = ReadU32LE(dec, csOff);
            uint statusLenHi = ReadU32LE(dec, csOff + 4);
            if (statusLenHi != 0 || statusLen == 0 || statusLen > 64) return false;

            int strStart = csOff + 8;
            if (strStart + statusLen + 4 > dec.Length) return false;

            for (int i = 0; i < statusLen; i++) {
                byte c = dec[strStart + i];
                if (c < 0x20 || c >= 0x7F) return false;
            }
            return true;
        }

        private CombatState? ParseCombatState(byte[] dec, int statsOffset) {
            int statusStart = statsOffset + 84;

            if (statusStart + 8 + 1 + 4 > dec.Length) return null;

            uint statusLen = ReadU32LE(dec, statusStart);
            uint statusLenHi = ReadU32LE(dec, statusStart + 4);

            if (statusLenHi != 0 || statusLen > 64 || statusLen == 0) return null;

            int stringStart = statusStart + 8;
            if (stringStart + statusLen + 4 > dec.Length) return null;

            string statusEffect = ReadAscii(dec, stringStart, (int)statusLen);
            int hpOffset = stringStart + (int)statusLen;
            uint hp = ReadU32LE(dec, hpOffset);

            return new CombatState {
                StatusEffect = statusEffect,
                Hp = (int)hp,
                HpOffset = hpOffset,
                StatusOffset = statusStart
            };
        }

        private (string? ClassName, int? BirthdayDay, int? BirthdayOffset) FindBirthdayInfo(byte[] dec, int? currentDay = null) {
            int n = dec.Length;
            if (n < 64) return (null, null, null);

            const int AGE_CAP = 500_000;

            bool Accept(int bday) {
                if (!currentDay.HasValue) return true;
                int age = currentDay.Value - bday;
                return age >= 0 && age <= AGE_CAP;
            }

            (string ClassName, int BirthdayDay, int BirthdayOffset)? ScanRange(int start, int end) {
                (string, int, int)? best = null;

                for (int off = start; off < Math.Max(start, end - 8); off++) {
                    if (off + 8 > n) break;

                    int ln = (int)ReadU64LE(dec, off);
                    if (ln < 3 || ln > 64) continue;

                    int strOff = off + 8;
                    int strEnd = strOff + ln;
                    int bdayOff = strEnd + 12;

                    if (bdayOff + 16 > n) continue;

                    if (!IsAsciiIdent(dec, strOff, ln)) continue;

                    int bday = (int)ReadI64LE(dec, bdayOff);
                    long sentinel = ReadI64LE(dec, bdayOff + 8);
                    if (sentinel != -1) continue;

                    if (!Accept(bday)) continue;

                    string cls = ReadAscii(dec, strOff, ln);

                    if (best == null || bdayOff > best.Value.Item3) {
                        best = (cls, bday, bdayOff);
                    }
                }

                return best;
            }

            int tail = 2048;
            var found = ScanRange(Math.Max(0, n - tail), n);
            if (found.HasValue)
                return (found.Value.ClassName, found.Value.BirthdayDay, found.Value.BirthdayOffset);

            var full = ScanRange(0, n);
            if (full.HasValue)
                return (full.Value.ClassName, full.Value.BirthdayDay, full.Value.BirthdayOffset);

            return (null, null, null);
        }

        private MutationResult ParseMutationTable(byte[] dec) {
            const int MUT_TABLE_SIZE = 16 + 14 * 20;
            int n = dec.Length;
            if (n < MUT_TABLE_SIZE) return new MutationResult();

            int bestScore = -1;
            int? bestOff = null;

            for (int base_ = 0; base_ <= n - MUT_TABLE_SIZE; base_++) {
                float s = ReadF32LE(dec, base_);
                uint coat = ReadU32LE(dec, base_ + 4);
                uint t1 = ReadU32LE(dec, base_ + 8);
                uint t2 = ReadU32LE(dec, base_ + 12);

                if (s < 0.05f || s > 20.0f) continue;
                if (coat == 0 || coat > 20000) continue;
                if (t1 > 500) continue;
                if (t2 != 0xFFFFFFFF && t2 > 5000) continue;

                int ok = 0;
                for (int i = 0; i < 14; i++) {
                    int off = base_ + 16 + i * 20;
                    uint c = ReadU32LE(dec, off + 4);
                    if (c == coat || c == 0) ok++;
                }

                if (ok < 10) continue;

                int score = ok * 1000 + base_;
                if (score > bestScore) {
                    bestScore = score;
                    bestOff = base_;
                }
            }

            if (!bestOff.HasValue) return new MutationResult();

            int coatOff = bestOff.Value + 4;
            uint coatId = ReadU32LE(dec, coatOff);

            var slots = new List<MutationSlot>();
            for (int i = 0; i < 14; i++) {
                int off = bestOff.Value + 16 + i * 20;
                int slotIndex = i + 1;
                var info = MutationSlotInfo.GetInfo(slotIndex);

                slots.Add(new MutationSlot {
                    SlotIndex = slotIndex,
                    Label = info.Label,
                    Category = info.Category,
                    SlotId = (int)ReadU32LE(dec, off),
                    Offset = off
                });
            }

            return new MutationResult {
                BaseOffset = bestOff,
                CoatId = (int)coatId,
                CoatOffset = coatOff,
                Slots = slots
            };
        }

        private List<EquipSlot>? ParseEquipmentSlots(byte[] dec) {
            byte[] EQUIP_HDR_PAT = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x05, 0x00, 0x00, 0x00 };
            byte[] EMPTY_MARKER = new byte[] { 0x00, 0x05, 0x00, 0x00, 0x00 };

            int searchStart = 0;
            while (true) {
                int hdr = FindBytes(dec, EQUIP_HDR_PAT, searchStart);
                if (hdr < 0) return null;

                var result = TryParseSlotsFromHeader(dec, hdr, EMPTY_MARKER);
                if (result != null) return result;

                searchStart = hdr + 1;
            }
        }

        private List<EquipSlot>? TryParseSlotsFromHeader(byte[] dec, int hdr, byte[] EMPTY_MARKER) {
            int pos = hdr + 8;
            var slots = new List<EquipSlot>();

            // Slots 0-3
            for (int bslot = 0; bslot < 4; bslot++) {
                if (pos + 5 <= dec.Length && SliceBytesEqual(dec, pos, EMPTY_MARKER, 5)) {
                    slots.Add(new EquipSlot { BlobSlot = bslot, Start = pos, End = pos + 5, ItemId = null, ImplicitEmpty = false });
                    pos += 5;
                    continue;
                }

                if (pos >= dec.Length || dec[pos] != 1) return null;

                var item = ReadItemString(dec, pos + 1);
                if (item == null) return null;

                int? nxt = FindSlotBoundary(dec, pos + 9 + item.Value.len);
                if (nxt == null) return null;

                slots.Add(new EquipSlot { BlobSlot = bslot, Start = pos, End = nxt.Value, ItemId = item.Value.id, ImplicitEmpty = false });
                pos = nxt.Value;
            }

            // Slot 4
            if (pos + 5 <= dec.Length && SliceBytesEqual(dec, pos, EMPTY_MARKER, 5)) {
                slots.Add(new EquipSlot { BlobSlot = 4, Start = pos, End = pos + 5, ItemId = null, ImplicitEmpty = false });
                return slots;
            }

            if (pos < dec.Length && dec[pos] == 1) {
                var item = ReadItemString(dec, pos + 1);
                if (item == null) return null;

                int? nxt = FindLastSlotBoundary(dec, pos + 9 + item.Value.len);
                if (nxt == null) return null;

                slots.Add(new EquipSlot { BlobSlot = 4, Start = pos, End = nxt.Value, ItemId = item.Value.id, ImplicitEmpty = false });
                return slots;
            }

            if (pos + 9 <= dec.Length && dec[pos] <= 3) {
                int ln = (int)ReadU64LE(dec, pos + 1);
                if (ln > 0 && ln <= 32 && pos + 9 + ln <= dec.Length && IsAsciiIdent(dec, pos + 9, ln)) {
                    slots.Add(new EquipSlot { BlobSlot = 4, Start = pos, End = pos, ItemId = null, ImplicitEmpty = true });
                    return slots;
                }

                int e = pos + 9;
                while (e < dec.Length && e < pos + 41 && dec[e] >= 0x20 && dec[e] < 0x7F) e++;
                int ntLen = e - (pos + 9);
                if (ntLen >= 2 && ntLen <= 32 && IsAsciiIdent(dec, pos + 9, ntLen)) {
                    slots.Add(new EquipSlot { BlobSlot = 4, Start = pos, End = pos, ItemId = null, ImplicitEmpty = true });
                    return slots;
                }
            }

            return null;
        }

        private (string id, int len)? ReadItemString(byte[] dec, int u64Off, int maxLen = 128) {
            if (u64Off + 8 > dec.Length) return null;

            int ln = (int)ReadU64LE(dec, u64Off);
            if (ln > 0 && ln <= maxLen && u64Off + 8 + ln <= dec.Length && IsAsciiIdent(dec, u64Off + 8, ln)) {
                return (ReadAscii(dec, u64Off + 8, ln), ln);
            }

            int strStart = u64Off + 8;
            int end = strStart;
            while (end < dec.Length && end < strStart + maxLen && dec[end] != 0) end++;
            int ntLen = end - strStart;
            if (ntLen > 0 && IsAsciiIdent(dec, strStart, ntLen)) {
                return (ReadAscii(dec, strStart, ntLen), ntLen);
            }

            return null;
        }

        private int? FindSlotBoundary(byte[] dec, int searchStart, int maxSearch = 800) {
            int n = dec.Length;
            int end = Math.Min(n - 6, searchStart + maxSearch);

            for (int p = searchStart; p < end; p++) {
                if (dec[p] == 0xFF && dec[p + 2] == 0x05 && dec[p + 3] == 0x00 && dec[p + 4] == 0x00 && dec[p + 5] == 0x00) {
                    return p + 6;
                }
            }
            return null;
        }

        private int? FindLastSlotBoundary(byte[] dec, int searchStart, int maxSearch = 1400) {
            int n = dec.Length;
            int end = Math.Min(n - 10, searchStart + maxSearch);

            for (int p = searchStart; p < end; p++) {
                if (dec[p] != 0xFF) continue;
                int tag = dec[p + 1];
                if (tag > 3) continue;
                if (p + 10 > n) continue;

                int ln = (int)ReadU64LE(dec, p + 2);
                if (ln > 0 && ln <= 32 && p + 10 + ln <= n && IsAsciiIdent(dec, p + 10, ln)) {
                    return p + 1;
                }

                int e = p + 10;
                while (e < n && e < p + 42 && dec[e] >= 0x20 && dec[e] < 0x7F) e++;
                int ntLen = e - (p + 10);
                if (ntLen >= 2 && ntLen <= 32 && IsAsciiIdent(dec, p + 10, ntLen)) {
                    return p + 1;
                }
            }
            return null;
        }

        private List<AbilitySlot> BuildAbilitySlots(byte[] dec) {
            var result = FindPrimaryAbilityRun(dec);
            if (result == null) return new List<AbilitySlot>();

            var slots = new List<AbilitySlot>();
            string[] activeLabels = { "Active1 (DefaultMove)", "Active2 (BasicAttack)", "Active3", "Active4", "Active5", "Active6" };

            var runItems = result.Value.runItems;
            var runEnd = result.Value.runEnd;
            var passive1Tier = result.Value.passive1Tier;
            var tailEntries = result.Value.tailEntries;

            int runStart = runItems.Count > 0 ? runItems[0].Offset : 0;

            for (int i = 0; i < 6 && i < runItems.Count; i++) {
                slots.Add(new AbilitySlot {
                    Label = activeLabels[i],
                    Kind = "u64run",
                    AbilityId = runItems[i].Value,
                    RunStart = runStart,
                    RunEnd = runEnd,
                    RunIndex = i
                });
            }

            if (runItems.Count > 10) {
                slots.Add(new AbilitySlot {
                    Label = "Passive1",
                    Kind = "u64run",
                    AbilityId = runItems[10].Value,
                    Tier = passive1Tier,
                    RunStart = runStart,
                    RunEnd = runEnd,
                    RunIndex = 10
                });
            }

            string[] tailLabels = { "Passive2", "Disorder1", "Disorder2" };
            for (int i = 0; i < tailEntries.Count; i++) {
                var entry = tailEntries[i];
                slots.Add(new AbilitySlot {
                    Label = tailLabels[i],
                    Kind = "u64tier",
                    AbilityId = entry.Value,
                    Tier = entry.Tier,
                    RecordOffset = entry.Offset,
                    ByteLength = entry.ByteLength
                });
            }

            return slots;
        }
        private (List<U64Str> runItems, int runEnd, int passive1Tier, List<U64StrTier> tailEntries)? FindPrimaryAbilityRun(byte[] dec) {
            int n = dec.Length;

            for (int start = 0; start < n - 32; start++) {
                var (items, end) = ParseU64Run(dec, start, 96, 32);
                if (items.Count < 11) continue;
                if (items[0].Value != "DefaultMove") continue;

                if (end + 4 > n) continue;
                int p1Tier = (int)ReadU32LE(dec, end);
                if (p1Tier <= 0 || p1Tier > 50) continue;

                var tail = ParseU64TierEntries(dec, end + 4, 3, 96);
                if (tail.Count != 3) continue;

                return (items, end, p1Tier, tail);
            }

            return null;
        }

        private (List<U64Str> Items, int End) ParseU64Run(byte[] dec, int start, int maxLen = 96, int maxItems = 64) {
            var items = new List<U64Str>();
            int i = start;
            int n = dec.Length;

            for (int count = 0; count < maxItems; count++) {
                if (i + 8 > n) break;

                int ln = (int)ReadU64LE(dec, i);
                if (ln <= 0 || ln > maxLen || i + 8 + ln > n) break;

                if (!IsAsciiIdent(dec, i + 8, ln)) break;

                string s = ReadAscii(dec, i + 8, ln);
                if (!System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z_][A-Za-z0-9_]*$")) break;

                items.Add(new U64Str { Offset = i, ByteLength = ln, Value = s });
                i += 8 + ln;
            }

            return (items, i);
        }

        private List<U64StrTier> ParseU64TierEntries(byte[] dec, int startOff, int count = 3, int maxLen = 96) {
            var outList = new List<U64StrTier>();
            int o = startOff;
            int n = dec.Length;

            for (int i = 0; i < count; i++) {
                if (o + 8 > n) break;

                int ln = (int)ReadU64LE(dec, o);
                if (ln <= 0 || ln > maxLen || o + 8 + ln + 4 > n) break;

                if (!IsAsciiIdent(dec, o + 8, ln)) break;
                string s = ReadAscii(dec, o + 8, ln);

                int tier = (int)ReadU32LE(dec, o + 8 + ln);
                if (tier <= 0 || tier > 50) break;

                outList.Add(new U64StrTier { Offset = o, ByteLength = ln, Value = s, Tier = tier });
                o = o + 8 + ln + 4;
            }

            return outList;
        }

        private CatStats ArrayToStats(int[] vals) {
            if (vals.Length < 7) return new CatStats();
            return new CatStats {
                Str = vals[0],
                Dex = vals[1],
                Con = vals[2],
                Int = vals[3],
                Spd = vals[4],
                Cha = vals[5],
                Luck = vals[6]
            };
        }
    }
}