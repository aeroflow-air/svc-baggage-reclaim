using System.Security.Cryptography;
using System.Text;

namespace AeroFlow.BaggageReclaim.Domain;

/// <summary>
/// Deterministic event ids (ADR-0017): a name-based UUID (RFC 9562 version 5, SHA-1) of the triggering event id and a
/// name such as the outgoing event type. The same input always gives the same id, so a re-publish carries the same
/// Service Bus <c>MessageId</c> and consumers can de-duplicate by <c>EventId</c>. Pure: no clock, no randomness.
/// </summary>
public static class EventIds
{
    public static Guid Derive(Guid source, string name)
    {
        Span<byte> ns = stackalloc byte[16];
        source.TryWriteBytes(ns, bigEndian: true, out _);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[16 + nameBytes.Length];
        ns.CopyTo(input);
        nameBytes.CopyTo(input, 16);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant
        return new Guid(bytes, bigEndian: true);
    }
}
