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

using CoiniumServ.Algorithms;
using CoiniumServ.Configuration;
using CoiniumServ.Container;
using CoiniumServ.Daemon.Exceptions;
using CoiniumServ.Persistance.Layers;
using CoiniumServ.Server.Web.Models.Algorithm;
using CoiniumServ.Shares;
using Nancy;
using Nancy.CustomErrors;
using Nancy.Helpers;
using Serilog;
using System.Linq;

namespace CoiniumServ.Server.Web.Modules
{
    public class BlockNotifyModule : NancyModule
    {

        public BlockNotifyModule(IObjectFactory objectFactory, IConfigManager configManager, IStorageLayer storageLayer)
            : base("/blocknotify")
        {
            Get["/{coinid}/{blockhash}"] = _ =>
            {
                var blockHash = _.blockhash;
                foreach (var pool in configManager.PoolConfigs)
                {
                   
                     
                    var _daemonClient = objectFactory.GetDaemonClient(pool.Daemon, pool.Coin);
                    var _logger = Log.ForContext<ShareManager>().ForContext("Component", pool.Coin.Name);
                    try
                    {
                        Daemon.Responses.Block block = _daemonClient.GetBlock(blockHash); // query the block.

                        if (block == null) // make sure the block exists
                            return false;

                        if (block.Confirmations == -1) // make sure the block is accepted.
                        {
                            _logger.Debug("Submitted block [{0}] is orphaned; [{1:l}]", block.Height, block.Hash);
                            return false;
                        }

                        //var expectedTxHash = share.CoinbaseHash.Bytes.ReverseBuffer().ToHexString(); // calculate our expected generation transactions's hash
                        //var genTxHash = block.Tx.First(); // read the hash of very first (generation transaction) of the block

                        //if (expectedTxHash != genTxHash) // make sure our calculated generated transaction and one reported by coin daemon matches.
                        //{
                        //    _logger.Debug("Submitted block [{0}] doesn't seem to belong us as reported generation transaction hash [{1:l}] doesn't match our expected one [{2:l}]", block.Height, genTxHash, expectedTxHash);
                        //    return false;
                        //}

                        var genTx = _daemonClient.GetTransaction(block.Tx.First()); // get the generation transaction.

                        // make sure we were able to read the generation transaction
                        if (genTx == null)
                        {
                            _logger.Debug("Submitted block [{0}] doesn't seem to belong us as we can't read the generation transaction on our records [{1:l}]", block.Height, block.Tx.First());
                            return false;
                        }
                        string _poolAccount = "";
                        try
                        {
                            _poolAccount = !pool.Coin.Options.UseDefaultAccount // if UseDefaultAccount is not set
                                ? _daemonClient.GetAccount(pool.Wallet.Adress) // find the account of the our pool address.
                                : ""; // use the default account.
                        }
                        catch (RpcException e)
                        {
                            _logger.Error("Error getting account for pool central wallet address: {0:l} - {1:l}", pool.Wallet.Adress, e.Message);
                        }

                        var poolOutput = genTx.GetPoolOutput(pool.Wallet.Adress, _poolAccount); // get the output that targets pool's central address.

                        // make sure the blocks generation transaction contains our central pool wallet address
                        if (poolOutput == null)
                        {
                            _logger.Debug("Submitted block [{0}] doesn't seem to belong us as generation transaction doesn't contain an output for pool's central wallet address: {0:}", block.Height, pool.Wallet.Adress);
                            return false;
                        }

                        // if the code flows here, then it means the block was succesfully submitted and belongs to us.
                        // share.SetFoundBlock(block, genTx); // assign the block to share.

                        _logger.Information("Found block [{0}] with hash [{1:l}]", block.Height, block.Hash);


                        storageLayer.AddBlock(block, genTx);
                        storageLayer.MoveCurrentShares(block.Height); 
                        return true;
                    }
                    catch (RpcException e)
                    {
                        // unlike BlockProcessor's detailed exception handling and decision making based on the error,
                        // here in share-manager we only one-shot submissions. If we get an error, basically we just don't care about the rest
                        // and flag the submission as failed.
                        _logger.Debug("We thought a block was found but while loading it back it was not found or had an error; [{0:l}] - reason; {1:l}", _.blockhash, e.Message);
                        return false;
                    }

                }

                return _.coinid + ":" + _.blockhash;
            };
        }
    }
}
