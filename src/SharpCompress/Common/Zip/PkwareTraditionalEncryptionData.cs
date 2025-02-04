using System;
using SharpCompress.Common.Zip.Headers;
using SharpCompress.Compressors.Deflate;

namespace SharpCompress.Common.Zip;

internal class PkwareTraditionalEncryptionData
{
    private static readonly CRC32 CRC32 = new CRC32();
    private readonly uint[] _keys = { 0x12345678, 0x23456789, 0x34567890 };
    private readonly ArchiveEncoding _archiveEncoding;

    private PkwareTraditionalEncryptionData(string password, ArchiveEncoding archiveEncoding)
    {
        _archiveEncoding = archiveEncoding;
        Initialize(password);
    }

    private byte MagicByte
    {
        get
        {
            var t = (ushort)((ushort)(_keys[2] & 0xFFFF) | 2);
            return (byte)((t * (t ^ 1)) >> 8);
        }
    }

    public static PkwareTraditionalEncryptionData ForRead(
        string password,
        ZipFileEntry header,
        byte[] encryptionHeader
    )
    {
        var encryptor = new PkwareTraditionalEncryptionData(password, header.ArchiveEncoding);
        var plainTextHeader = encryptor.Decrypt(encryptionHeader, encryptionHeader.Length);
        if (plainTextHeader[11] != (byte)((header.Crc >> 24) & 0xff))
        {
            if (!FlagUtility.HasFlag(header.Flags, HeaderFlags.UsePostDataDescriptor))
            {
                throw new CryptographicException("The password did not match.");
            }
            if (plainTextHeader[11] != (byte)((header.OriginalLastModifiedTime >> 8) & 0xff))
            {
                throw new CryptographicException("The password did not match.");
            }
        }
        return encryptor;
    }

    public byte[] Decrypt(byte[] cipherText, int length)
    {
        if (length > cipherText.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Bad length during Decryption: the length parameter must be smaller than or equal to the size of the destination array."
            );
        }

        var plainText = new byte[length];
        for (var i = 0; i < length; i++)
        {
            var c = (byte)(cipherText[i] ^ MagicByte);
            UpdateKeys(c);
            plainText[i] = c;
        }
        return plainText;
    }

    public byte[] Encrypt(byte[] plainText, int length)
    {
        if (plainText is null)
        {
            throw new ArgumentNullException(nameof(plainText));
        }

        if (length > plainText.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length),
                "Bad length during Encryption: The length parameter must be smaller than or equal to the size of the destination array."
            );
        }

        var cipherText = new byte[length];
        for (var i = 0; i < length; i++)
        {
            var c = plainText[i];
            cipherText[i] = (byte)(plainText[i] ^ MagicByte);
            UpdateKeys(c);
        }
        return cipherText;
    }

    private void Initialize(string password)
    {
        var p = StringToByteArray(password);
        for (var i = 0; i < password.Length; i++)
        {
            UpdateKeys(p[i]);
        }
    }

    internal byte[] StringToByteArray(string value)
    {
        var a = _archiveEncoding.Password.GetBytes(value);
        return a;
    }

    private void UpdateKeys(byte byteValue)
    {
        _keys[0] = (uint)CRC32.ComputeCrc32((int)_keys[0], byteValue);
        _keys[1] = _keys[1] + (byte)_keys[0];
        _keys[1] = (_keys[1] * 0x08088405) + 1;
        _keys[2] = (uint)CRC32.ComputeCrc32((int)_keys[2], (byte)(_keys[1] >> 24));
    }
}
