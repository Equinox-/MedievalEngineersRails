using System;
using VRageMath;

namespace Equinox76561198048419394.RailSystem.Util
{
    public class MemStream
    {
        private byte[] _backing;
        private int _head;

        public MemStream(byte[] backing)
        {
            _backing = backing;
        }

        public MemStream(int capacity)
        {
            _backing = new byte[capacity];
        }
        
        private void EnsureRemaining(int len)
        {
            if (_backing.Length >= _head + len)
                return;
            Array.Resize(ref _backing, MathHelper.GetNearestBiggerPowerOfTwo(_head + len));
        }

        public void Write7BitEncoded(ulong val)
        {
            do
            {
                var low = val & 0x7F;
                val >>= 7;
                if (val > 0)
                    low |= 0x80;
                WriteByte((byte) low);
            } while (val > 0);
        }

        public ulong Read7BitEncoded()
        {
            var shift = 0;
            var result = 0UL;
            while (true)
            {
                var val = ReadByte();
                result |= (val & 0x7FUL) << shift;
                shift += 7;
                if ((val & 0x80) == 0) break;
            }
            return result;
        }

        public void WriteByte(byte b)
        {
            EnsureRemaining(1);
            _backing[_head++] = b;
        }

        public byte ReadByte()
        {
            return _backing[_head++];
        }

        public byte[] Buffer => _backing;

        public byte[] ToArray()
        {
            var res = new byte[_head];
            Array.Copy(_backing, res, res.Length);
            return res;
        }

        public string ToBase64()
        {
            return Convert.ToBase64String(_backing, 0, _head);
        }
    }
}