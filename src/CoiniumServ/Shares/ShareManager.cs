﻿#region License
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
using System.Diagnostics;
using System.Linq;
using AustinHarris.JsonRpc;
using CoiniumServ.Daemon;
using CoiniumServ.Daemon.Exceptions;
using CoiniumServ.Jobs.Tracker;
using CoiniumServ.Persistance.Layers;
using CoiniumServ.Pools;
using CoiniumServ.Server.Mining.Getwork;
using CoiniumServ.Server.Mining.Stratum;
using CoiniumServ.Server.Mining.Stratum.Errors;
using CoiniumServ.Utils.Extensions;
using Serilog;

namespace CoiniumServ.Shares
{
    public class ShareManager : IShareManager
    {
        public event EventHandler BlockFound;

        public event EventHandler ShareSubmitted;

        private readonly IJobTracker _jobTracker;

        private readonly IDaemonClient _daemonClient;

        private readonly IStorageLayer _storageLayer;

        private readonly IPoolConfig _poolConfig;

        private string _poolAccount;

        private readonly ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="ShareManager" /> class.
        /// </summary>
        /// <param name="poolConfig"></param>
        /// <param name="daemonClient"></param>
        /// <param name="jobTracker"></param>
        /// <param name="storageLayer"></param>
        public ShareManager(IPoolConfig poolConfig, IDaemonClient daemonClient, IJobTracker jobTracker, IStorageLayer storageLayer)
        {
            _poolConfig = poolConfig;
            _daemonClient = daemonClient;
            _jobTracker = jobTracker;
            _storageLayer = storageLayer;
            _logger = Log.ForContext<ShareManager>().ForContext("Component", poolConfig.Coin.Name);

            FindPoolAccount();
        }

        /// <summary>
        /// Processes the share.
        /// </summary>
        /// <param name="miner">The miner.</param>
        /// <param name="jobId">The job identifier.</param>
        /// <param name="extraNonce2">The extra nonce2.</param>
        /// <param name="nTimeString">The n time string.</param>
        /// <param name="nonceString">The nonce string.</param>
        /// <returns></returns>
        public IShare ProcessShare(IStratumMiner miner, string jobId, string extraNonce2, string nTimeString, string nonceString)
        {
            // check if the job exists
            var id = Convert.ToUInt64(jobId, 16);
            var job = _jobTracker.Get(id);


            // create the share
            try
            {
                var share = new Share(miner, id, job, extraNonce2, nTimeString, nonceString);
                _logger.Debug("Share submitted at {0:0.00} : " + share.IsValid);
                if (share.IsValid)
                    HandleValidShare(share);
                else
                    HandleInvalidShare(share);

                OnShareSubmitted(new ShareEventArgs(miner));  // notify the listeners about the share.

                return share;
            }
            catch (Exception e)
            {
                _logger.Error("Error occured in share: " + e.Message);
                throw e;
            }


        }

        public IShare ProcessShare(IGetworkMiner miner, string data)
        {
            throw new NotImplementedException();
        }

        private void HandleValidShare(IShare share)
        {
            var miner = (IStratumMiner)share.Miner;
            miner.ValidShareCount++;

            _storageLayer.AddShare(share); // commit the share.
            _logger.Debug("Share accepted at {0:0.00}/{1} by miner {2:l}", share.Difficulty, miner.Difficulty, miner.Username);

            // check if share is a block candidate
            if (!share.IsBlockCandidate)
                return;

            // submit block candidate to daemon.
            var accepted = SubmitBlock(share);

            if (!accepted) // if block wasn't accepted
                return; // just return as we don't need to notify about it and store it.

            OnBlockFound(EventArgs.Empty); // notify the listeners about the new block.
            
            
        }

        private void HandleInvalidShare(IShare share)
        {
            var miner = (IStratumMiner)share.Miner;
            miner.InvalidShareCount++;

            JsonRpcException exception = null; // the exception determined by the stratum error code.
            switch (share.Error)
            {
                case ShareError.DuplicateShare:
                    exception = new DuplicateShareError(share.Nonce);
                    break;
                case ShareError.IncorrectExtraNonce2Size:
                    exception = new OtherError("Incorrect extranonce2 size");
                    break;
                case ShareError.IncorrectNTimeSize:
                    exception = new OtherError("Incorrect nTime size");
                    break;
                case ShareError.IncorrectNonceSize:
                    exception = new OtherError("Incorrect nonce size");
                    break;
                case ShareError.JobNotFound:
                    exception = new JobNotFoundError(share.JobId);
                    break;
                case ShareError.LowDifficultyShare:
                    exception = new LowDifficultyShare(share.Difficulty);
                    break;
                case ShareError.NTimeOutOfRange:
                    exception = new OtherError("nTime out of range");
                    break;
            }
            JsonRpcContext.SetException(exception); // set the stratum exception within the json-rpc reply.

            Debug.Assert(exception != null); // exception should be never null when the share is marked as invalid.
            _logger.Debug("Rejected share by miner {0:l}, reason: {1:l}", miner.Username, exception.message);
        }

        private bool SubmitBlock(IShare share)
        {
            // TODO: we should try different submission techniques and probably more then once: https://github.com/ahmedbodi/stratum-mining/blob/master/lib/bitcoin_rpc.py#L65-123
            int attempts = 0;
            bool rValue = false;
            while (attempts < 5)
            {
                attempts++;
                try
                {
                    if (_poolConfig.Coin.Options.SubmitBlockSupported) // see if submitblock() is available.
                    {

                        var result = _daemonClient.SubmitBlock(share.BlockHex.ToHexString()); // submit the block.
                        _logger.Debug("Submit Block Result via SubmitBlock;  [{0:l}]   [{1:1}] ", share.BlockHash.ToHexString(), result);
                        if (result == null)
                        {
                            rValue = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        var result = _daemonClient.GetBlockTemplate(share.BlockHex.ToHexString()); // use getblocktemplate() if submitblock() is not supported.

                        _logger.Debug("Submit Block Result via getBlockTemplate;  [{0:l}]   [{1:1}] ", share.BlockHash.ToHexString(), result);
                        if (result == null)
                        {
                            rValue = true;
                            break;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (RpcException e)
                {
                    // unlike BlockProcessor's detailed exception handling and decision making based on the error,
                    // here in share-manager we only one-shot submissions. If we get an error, basically we just don't care about the rest
                    // and flag the submission as failed.
                    _logger.Debug("We thought a block was found but it was rejected by the coin daemon; [{0:l}] - reason; {1:l}", share.BlockHash.ToHexString(), e.Message);
                    continue;
                }
            }
            return rValue;
        }


        private void OnBlockFound(EventArgs e)
        {
            var handler = BlockFound;

            if (handler != null)
                handler(this, e);
        }

        private void OnShareSubmitted(EventArgs e)
        {
            var handler = ShareSubmitted;

            if (handler != null)
                handler(this, e);
        }

        private void FindPoolAccount()
        {
            try
            {
                _poolAccount = !_poolConfig.Coin.Options.UseDefaultAccount // if UseDefaultAccount is not set
                    ? _daemonClient.GetAccount(_poolConfig.Wallet.Adress) // find the account of the our pool address.
                    : ""; // use the default account.
            }
            catch (RpcException e)
            {
                _logger.Error("Error getting account for pool central wallet address: {0:l} - {1:l}", _poolConfig.Wallet.Adress, e.Message);
            }
        }
    }
}
