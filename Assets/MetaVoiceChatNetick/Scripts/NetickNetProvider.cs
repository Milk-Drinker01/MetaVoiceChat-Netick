using System;
using System.Collections.Generic;
using Netick.Unity;
using UnityEngine;

// A possible optimization is to handle all of the networking in one manager class and batch frames with a single timestamp.
// However, this is complex and benefits are negligible.

namespace MetaVoiceChat.NetProviders.Netick
{
    [RequireComponent(typeof(MetaVc))]
    public class NetickNetProvider : NetworkBehaviour, INetProvider
    {
        #region Singleton
        public static NetickNetProvider LocalPlayerInstance { get; private set; }
        private readonly static List<NetickNetProvider> instances = new();
        public static IReadOnlyList<NetickNetProvider> Instances => instances;
        #endregion

        bool INetProvider.IsLocalPlayerDeafened => LocalPlayerInstance.MetaVc.isDeafened;

        public MetaVc MetaVc { get; private set; }

        private MetaVoiceChatNetick VoiceDataTransmitter;
        private int playerID;

        public override void NetworkStart()
        {
            Sandbox.Log(IsInputSource);
            #region Singleton
            if (IsInputSource)
            {
                LocalPlayerInstance = this;
            }

            instances.Add(this);
            #endregion
            
            if (Sandbox.TryGetComponent<MetaVoiceChatNetick>(out VoiceDataTransmitter))
            {
                if (Sandbox.IsServer)
                {
                    playerID = InputSource.PlayerId;
                    VoiceDataTransmitter.ConnectionIdToPlayerObjectID.Add(playerID, Object.Id);
                }
            }
            else
                Debug.LogError("Your Sandbox Prefab doesnt have the MetaVoiceChatNetick component");

            static int GetMaxDataBytesPerPacket()
            {
                //int bytes = NetworkMessages.MaxMessageSize(Channels.Unreliable) - 13;
                int bytes = 1000;
                bytes -= sizeof(int); // Index
                bytes -= sizeof(double); // Timestamp
                bytes -= sizeof(byte); // Additional latency
                bytes -= sizeof(int); // Player id
                bytes -= sizeof(ushort); // Array length
                return bytes;
            }
            //Sandbox.Transport.p
            MetaVc = GetComponent<MetaVc>();
            MetaVc.StartClient(this, IsInputSource, GetMaxDataBytesPerPacket());
        }

        public override void NetworkDestroy()
        {
            #region Singleton
            if (IsInputSource)
            {
                LocalPlayerInstance = null;
            }

            instances.Remove(this);
            #endregion

            MetaVc.StopClient();

            if (Sandbox.IsServer && VoiceDataTransmitter != null)
                VoiceDataTransmitter.ConnectionIdToPlayerObjectID.Remove(playerID);
        }

        void INetProvider.RelayFrame(int index, double timestamp, ReadOnlySpan<byte> data)
        {
            //byte[] array = FixedLengthArrayPool<byte>.Rent(data.Length);
            //data.CopyTo(array);

            float additionalLatency = GetAdditionalLatency();

            if (Sandbox.IsServer)
            {
                //send the data to all clients
                VoiceDataTransmitter.SendServerVoiceToClients(index, timestamp, additionalLatency, data, playerID);
            }
            else
            {
                //send the data from client to server
                VoiceDataTransmitter.SendVoiceDataToServer(index, timestamp, additionalLatency, data);
            }
        }

        public static float GetAdditionalLatency()
        {
            return Time.deltaTime;
        }

        public void ReceiveFrame(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data)
        {
            MetaVc.ReceiveFrame(index, timestamp, additionalLatency, data);
        }
    }
}