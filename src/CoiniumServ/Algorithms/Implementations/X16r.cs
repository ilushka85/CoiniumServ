#region License
// 
//     MIT License
//
//     CoiniumServ - Crypto Currency Mining Pool Server Software
//     Copyright (C) 2013 - 2017, CoiniumServ Project
//     Hüseyin Uslu, shalafiraistlin at gmail dot com
//     https://github.com/bonesoul/CoiniumServ
// 
//     Permission is hereby granted, free of charge, to any person obtaining a copy
//     of this software and associated documentation files (the "Software"), to deal
//     in the Software without restriction, including without limitation the rights
//     to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//     copies of the Software, and to permit persons to whom the Software is
//     furnished to do so, subject to the following conditions:
//     
//     The above copyright notice and this permission notice shall be included in all
//     copies or substantial portions of the Software.
//     
//     THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//     IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//     FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//     AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//     LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//     OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//     SOFTWARE.
// 
#endregion

using System;
using System.Collections.Generic;
using HashLib;

namespace CoiniumServ.Algorithms.Implementations
{
    public sealed class X16r : IHashAlgorithm
    {
        public uint Multiplier { get; private set; }

        private readonly List<IHash> _hashers;

        public X16r()
        {
            _hashers = new List<IHash>
            {
                HashFactory.Crypto.SHA3.CreateBlake512(),
                HashFactory.Crypto.SHA3.CreateBlueMidnightWish512(),
                HashFactory.Crypto.SHA3.CreateGroestl512(),
                HashFactory.Crypto.SHA3.CreateSkein512(),
                HashFactory.Crypto.SHA3.CreateJH512(),
                HashFactory.Crypto.SHA3.CreateKeccak512(),
                HashFactory.Crypto.SHA3.CreateLuffa512(),
                HashFactory.Crypto.SHA3.CreateCubeHash512(),
                HashFactory.Crypto.SHA3.CreateSHAvite3_512(),
                HashFactory.Crypto.SHA3.CreateSIMD512(),
                HashFactory.Crypto.SHA3.CreateEcho512(),
                HashFactory.Crypto.SHA3.CreateHamsi512(),
                HashFactory.Crypto.SHA3.CreateFugue512(),
                HashFactory.Crypto.SHA3.CreateShabal512(),
                HashFactory.Crypto.CreateWhirlpool(),
                HashFactory.Crypto.CreateSHA512(),
            };

            Multiplier = 1;
        }


        private int GetHashSelection(int[] prevHash, int index)
        {
            int nibble = prevHash[4 + 7 - (index / 2)];
            if (index % 2 == 0)
            {
                return nibble >> 4;
            }
            else
            {
                return nibble & 0x0f;
            }
        }
        private string GetAlgoString(int[] input)
        {
            try
            {
                var output = "";
                for (int x = 0; x < _hashers.Count; x++)
                {
                    char b = (char)((15 - x) >> 1);
                    int algoDigit = (x & 1) != 0 ? input[b] & 0xf : input[b] >> 4;
                    if (algoDigit >= 10)
                    {
                        output = output + (char)('A' + (algoDigit - 10));
                    }
                    else
                    {
                        output = output + algoDigit;
                    }

                }
                return output;
            }
            catch (Exception e)
            {
                throw e;
            }

        }
        public byte[] Hash(byte[] input)
        {
            try
            {
                
                var buffer = input;
                var prevHash = Array.ConvertAll(input, Convert.ToInt32);
                for (int i = 0; i < 16; i++)
                {
                    var hashSelection = GetHashSelection(prevHash, i);
                    buffer = _hashers[hashSelection].ComputeBytes(buffer).GetBytes();

                }
                return buffer;

            }
            catch (Exception e)
            {
                throw e;
            }
        }
    }
}
