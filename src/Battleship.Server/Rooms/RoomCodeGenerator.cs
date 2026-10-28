using System.Security.Cryptography;
using Battleship.Shared;

namespace Battleship.Server.Rooms;

public class RoomCodeGenerator
{
    public virtual string Generate()
    {
        Span<char> code = stackalloc char[ProtocolInfo.RoomCodeLength];
        var alphabet = ProtocolInfo.RoomCodeAlphabet;
        for (var i = 0; i < code.Length; i++)
        {
            code[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(code);
    }
}
