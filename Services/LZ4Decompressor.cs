using LZ4;

namespace Mewgenics.SaveFileViewer.Services {
    public interface ILZ4Decompressor {
        Task<byte[]> DecompressAsync(byte[] compressedData);
    }

    public class LZ4Decompressor : ILZ4Decompressor {
        public async Task<byte[]> DecompressAsync(byte[] compressedData) {
            return await Task.Run(() => {
                try {
                    // Try with header (first 4 bytes contain original size)
                    if (compressedData.Length >= 4) {
                        int originalSize = BitConverter.ToInt32(compressedData, 0);
                        if (originalSize > 0 && originalSize < 10_000_000) {
                            byte[] decompressed = new byte[originalSize];
                            int decoded = LZ4Codec.Decode(
                                compressedData, 4, compressedData.Length - 4,
                                decompressed, 0, decompressed.Length,
                                true
                            );

                            if (decoded == originalSize)
                                return decompressed;
                        }
                    }

                    // Try without header - try common sizes for Mewgenics cats
                    int[] possibleSizes = { 0x2000, 0x3000, 0x4000, 0x5000, 0x6000, 0x8000 };

                    foreach (int size in possibleSizes) {
                        try {
                            byte[] decompressed = new byte[size];
                            int decoded = LZ4Codec.Decode(
                                compressedData, 0, compressedData.Length,
                                decompressed, 0, decompressed.Length,
                                false
                            );

                            if (decoded > 0 && decoded <= size) {
                                Array.Resize(ref decompressed, decoded);
                                return decompressed;
                            }
                        } catch {
                            continue;
                        }
                    }

                    throw new Exception("All decompression attempts failed");
                } catch (Exception ex) {
                    throw new Exception($"LZ4 decompression failed: {ex.Message}", ex);
                }
            });
        }
        public async Task WarmupAsync() {
            
            await Task.Run(() =>
            {
                byte[] testData = new byte[] { 0, 0, 0, 0, 0x10, 0, 0, 0 }; 
                try {
                    var _ = LZ4Codec.Decode(testData, 0, testData.Length, new byte[16], 0, 16, true);
                } catch {
                    
                }
            });
        }
    }
}