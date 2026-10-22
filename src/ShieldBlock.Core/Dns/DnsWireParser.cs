using System;
using System.Collections.Generic;
using System.Text;

namespace ShieldBlock.Core.Dns
{
    public enum DnsRecordType : ushort
    {
        A = 1,
        NS = 2,
        CNAME = 5,
        SOA = 6,
        PTR = 12,
        MX = 15,
        TXT = 16,
        AAAA = 28,
        SRV = 33,
        ANY = 255
    }

    public enum DnsResponseCode : byte
    {
        NoError = 0,
        FormatError = 1,
        ServerFailure = 2,
        NameError = 3,       // NXDOMAIN
        NotImplemented = 4,
        Refused = 5
    }

    public class DnsQuestion
    {
        public string Name { get; set; } = string.Empty;
        public DnsRecordType Type { get; set; }
        public ushort Class { get; set; } = 1; // IN (Internet)
    }

    public class DnsMessage
    {
        public ushort TransactionId { get; set; }
        public bool IsResponse { get; set; }
        public byte OpCode { get; set; }
        public bool AuthoritativeAnswer { get; set; }
        public bool Truncated { get; set; }
        public bool RecursionDesired { get; set; }
        public bool RecursionAvailable { get; set; }
        public DnsResponseCode ResponseCode { get; set; } = DnsResponseCode.NoError;

        public List<DnsQuestion> Questions { get; } = new();
        public byte[] RawBytes { get; set; } = Array.Empty<byte>();
    }

    public static class DnsWireParser
    {
        public static DnsMessage? Parse(byte[] buffer, int offset, int length)
        {
            if (length < 12) return null; // DNS header is 12 bytes minimum

            var msg = new DnsMessage
            {
                TransactionId = (ushort)((buffer[offset] << 8) | buffer[offset + 1])
            };

            ushort flags = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);
            msg.IsResponse = (flags & 0x8000) != 0;
            msg.OpCode = (byte)((flags >> 11) & 0x0F);
            msg.AuthoritativeAnswer = (flags & 0x0400) != 0;
            msg.Truncated = (flags & 0x0200) != 0;
            msg.RecursionDesired = (flags & 0x0100) != 0;
            msg.RecursionAvailable = (flags & 0x0080) != 0;
            msg.ResponseCode = (DnsResponseCode)(flags & 0x000F);

            ushort qdCount = (ushort)((buffer[offset + 4] << 8) | buffer[offset + 5]);

            int pos = offset + 12;
            for (int i = 0; i < qdCount; i++)
            {
                if (pos >= offset + length) break;
                string name = ReadDomainName(buffer, offset, length, ref pos);
                if (pos + 4 > offset + length) break;

                ushort qType = (ushort)((buffer[pos] << 8) | buffer[pos + 1]);
                ushort qClass = (ushort)((buffer[pos + 2] << 8) | buffer[pos + 3]);
                pos += 4;

                msg.Questions.Add(new DnsQuestion
                {
                    Name = name,
                    Type = (DnsRecordType)qType,
                    Class = qClass
                });
            }

            msg.RawBytes = new byte[length];
            Buffer.BlockCopy(buffer, offset, msg.RawBytes, 0, length);
            return msg;
        }

        private static string ReadDomainName(byte[] buffer, int baseOffset, int totalLength, ref int currentPos)
        {
            var sb = new StringBuilder();
            int jumpCount = 0;
            int maxJumps = 10;
            int pos = currentPos;
            bool jumped = false;

            while (pos < baseOffset + totalLength)
            {
                byte len = buffer[pos++];
                if (len == 0)
                {
                    break;
                }

                // Check for DNS pointer compression (0xC0)
                if ((len & 0xC0) == 0xC0)
                {
                    if (pos >= baseOffset + totalLength) break;
                    if (!jumped)
                    {
                        currentPos = pos + 1; // Advance original pointer past the 2-byte reference
                        jumped = true;
                    }

                    int offset = ((len & 0x3F) << 8) | buffer[pos];
                    pos = baseOffset + offset;
                    jumpCount++;
                    if (jumpCount > maxJumps) break; // Avoid pointer loops
                    continue;
                }

                if (pos + len > baseOffset + totalLength) break;
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(buffer, pos, len));
                pos += len;
            }

            if (!jumped)
            {
                currentPos = pos;
            }

            return sb.ToString().ToLowerInvariant();
        }

        public static byte[] CreateBlockedResponse(DnsMessage query, string blockedIp = "0.0.0.0", uint ttl = 300)
        {
            if (query.Questions.Count == 0)
            {
                return CreateNxDomainResponse(query);
            }

            var q = query.Questions[0];
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);

            // 1. Transaction ID
            writer.Write((byte)(query.TransactionId >> 8));
            writer.Write((byte)(query.TransactionId & 0xFF));

            // 2. Flags: QR=1 (Response), AA=1, RA=1, RCODE=0 (NoError) -> 0x8580
            writer.Write((byte)0x85);
            writer.Write((byte)0x80);

            // 3. QDCOUNT = 1, ANCOUNT = 1, NSCOUNT = 0, ARCOUNT = 0
            writer.Write((byte)0x00); writer.Write((byte)0x01); // 1 Question
            writer.Write((byte)0x00); writer.Write((byte)0x01); // 1 Answer
            writer.Write((byte)0x00); writer.Write((byte)0x00);
            writer.Write((byte)0x00); writer.Write((byte)0x00);

            // 4. Question Section
            WriteDomainName(writer, q.Name);
            writer.Write((byte)((ushort)q.Type >> 8));
            writer.Write((byte)((ushort)q.Type & 0xFF));
            writer.Write((byte)(q.Class >> 8));
            writer.Write((byte)(q.Class & 0xFF));

            // 5. Answer Section
            // Pointer to question name: 0xC00C (offset 12 in DNS header)
            writer.Write((byte)0xC0);
            writer.Write((byte)0x0C);

            if (q.Type == DnsRecordType.AAAA)
            {
                // AAAA: type=28, class=1, ttl=300, rdlength=16, rdata=:: (16 zero bytes)
                writer.Write((byte)0x00); writer.Write((byte)0x1C);
                writer.Write((byte)0x00); writer.Write((byte)0x01);
                WriteUInt32(writer, ttl);
                writer.Write((byte)0x00); writer.Write((byte)0x10); // 16 bytes
                for (int i = 0; i < 16; i++) writer.Write((byte)0x00);
            }
            else
            {
                // A: type=1, class=1, ttl=300, rdlength=4, rdata=0.0.0.0
                writer.Write((byte)0x00); writer.Write((byte)0x01);
                writer.Write((byte)0x00); writer.Write((byte)0x01);
                WriteUInt32(writer, ttl);
                writer.Write((byte)0x00); writer.Write((byte)0x04); // 4 bytes

                var ipParts = blockedIp.Split('.');
                if (ipParts.Length == 4)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        byte b = byte.TryParse(ipParts[i], out var parsed) ? parsed : (byte)0;
                        writer.Write(b);
                    }
                }
                else
                {
                    writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0); writer.Write((byte)0);
                }
            }

            return ms.ToArray();
        }

        public static byte[] CreateNxDomainResponse(DnsMessage query)
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);

            writer.Write((byte)(query.TransactionId >> 8));
            writer.Write((byte)(query.TransactionId & 0xFF));

            // Flags: QR=1, AA=1, RA=1, RCODE=3 (NXDOMAIN) -> 0x8583
            writer.Write((byte)0x85);
            writer.Write((byte)0x83);

            writer.Write((byte)0x00); writer.Write((byte)(query.Questions.Count > 0 ? 0x01 : 0x00));
            writer.Write((byte)0x00); writer.Write((byte)0x00);
            writer.Write((byte)0x00); writer.Write((byte)0x00);
            writer.Write((byte)0x00); writer.Write((byte)0x00);

            if (query.Questions.Count > 0)
            {
                var q = query.Questions[0];
                WriteDomainName(writer, q.Name);
                writer.Write((byte)((ushort)q.Type >> 8));
                writer.Write((byte)((ushort)q.Type & 0xFF));
                writer.Write((byte)(q.Class >> 8));
                writer.Write((byte)(q.Class & 0xFF));
            }

            return ms.ToArray();
        }

        private static void WriteDomainName(System.IO.BinaryWriter writer, string domain)
        {
            var parts = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                byte[] bytes = Encoding.ASCII.GetBytes(part);
                writer.Write((byte)bytes.Length);
                writer.Write(bytes);
            }
            writer.Write((byte)0); // Terminating null byte
        }

        private static void WriteUInt32(System.IO.BinaryWriter writer, uint value)
        {
            writer.Write((byte)((value >> 24) & 0xFF));
            writer.Write((byte)((value >> 16) & 0xFF));
            writer.Write((byte)((value >> 8) & 0xFF));
            writer.Write((byte)(value & 0xFF));
        }
    }
}
