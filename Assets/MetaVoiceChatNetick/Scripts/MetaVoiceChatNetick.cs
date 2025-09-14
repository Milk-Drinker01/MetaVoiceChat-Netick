#if NETICK
using Netick;
using Netick.Unity;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace MetaVoiceChat.NetProviders.Netick
{
    public class MetaVoiceChatNetick : NetickBehaviour
    {
        [Header("This Component should live on your Sandbox Prefab.")]
        [Space(10)]
        [Tooltip("Uses NetworkConnection.SendData - If you have other things that use this, make sure the IDs are unique")]
        public byte VoiceDataID = 0;

        public Dictionary<int, int> ConnectionIdToPlayerObjectID;

        private const float MaxAdditionalLatency = 0.2f;

        private byte[] voiceBuffer;
        private GCHandle pinnedBuffer;
        private IntPtr pVoiceBuffer;
        private int ExtraDataSize;
        public const int additionalLatencySize = sizeof(byte);  //in case we ever want to change the data type this is sent as

        public override unsafe void NetworkStart()
        {
            ConnectionIdToPlayerObjectID = new Dictionary<int, int>();
            Sandbox.Events.OnDataReceived += OnDataReceived;

            voiceBuffer = new byte[1024];
            pinnedBuffer = GCHandle.Alloc(voiceBuffer, GCHandleType.Pinned);
            pVoiceBuffer = pinnedBuffer.AddrOfPinnedObject();

            ExtraDataSize = (sizeof(int) + sizeof(double) + additionalLatencySize);
        }

        public override void NetworkDestroy()
        {
            if (pinnedBuffer.IsAllocated)
                pinnedBuffer.Free();
        }

        unsafe void OnDataReceived(NetworkSandbox sandbox, NetworkConnection sender, byte id, byte* data, int length, TransportDeliveryMethod transportDeliveryMethod)
        {
            if (id != VoiceDataID)
                return;

            int index = *(int*)data;
            data += sizeof(int);

            double timestamp = *(double*)data;
            data += sizeof(double);

            byte compressedAdditionalLatency = *(byte*)data;
            data += additionalLatencySize;

            float additionalLatency = (float)compressedAdditionalLatency / byte.MaxValue;
            additionalLatency *= MaxAdditionalLatency;

            int payloadLength = length;
            payloadLength-= ExtraDataSize;

            if (sandbox.IsServer)
            {
                Buffer.MemoryCopy(data, (byte*)pVoiceBuffer, voiceBuffer.Length, payloadLength);

                int userNetworkID = ConnectionIdToPlayerObjectID[sender.PlayerId];

                if (Sandbox.TryGetBehaviour<NetickNetProvider>(userNetworkID, out NetickNetProvider cl))
                    cl.ReceiveFrame(index, timestamp, additionalLatency, new ReadOnlySpan<byte>(data, payloadLength));

                //modify the additional latency in place
                float* pLatency = (float*)(data - sizeof(float));
                *pLatency += NetickNetProvider.GetAdditionalLatency();

                //send the data from server to all other clients
                SendVoiceDataToClients(sandbox, data, userNetworkID, length, sender);
            }
            else
            {
                payloadLength -= sizeof(int);   //clients receive an extra int for PlayerID

                //Buffer.MemoryCopy(data, (byte*)pVoiceBuffer, voiceBuffer.Length, payloadLength);

                data += payloadLength;

                int userNetworkID = *(int*)data;

                data -= payloadLength;

                if (Sandbox.TryGetBehaviour<NetickNetProvider>(userNetworkID, out NetickNetProvider cl))
                    cl.ReceiveFrame(index, timestamp, additionalLatency, new ReadOnlySpan<byte>(data, payloadLength));

                //if (Sandbox.TryGetBehaviour<NetickNetProvider>(userNetworkID, out NetickNetProvider cl))
                //    cl.ReceiveFrame(index, timestamp, additionalLatency, voiceBuffer.AsSpan(0, payloadLength));
            }
        }

        public unsafe void SendVoiceDataToClients(NetworkSandbox sandbox, byte* data, int playerID, int length, NetworkConnection clientsConnection = null)
        {
            //append player id to the end of the voice data buffer
            int totalLength = length + sizeof(int);
            byte* buffer = stackalloc byte[totalLength];

            Buffer.MemoryCopy(data, buffer, totalLength, length);
            *(int*)(buffer + length) = playerID;

            //send the voice chat data
            foreach (NetworkConnection conn in sandbox.ConnectedClients)
            {
                if (conn != clientsConnection)
                    conn.SendData(VoiceDataID, buffer, totalLength, TransportDeliveryMethod.Unreliable);
            }
        }
        
        private unsafe byte* GetVoiceDataPointer(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data)
        {
            additionalLatency = Mathf.Clamp(additionalLatency, 0, MaxAdditionalLatency);
            float t = additionalLatency / MaxAdditionalLatency;
            byte compressedAdditionalLatency = ((byte)(t * byte.MaxValue));

            byte* ptr = (byte*)pVoiceBuffer;

            *(int*)ptr = index;
            ptr += sizeof(int);

            *(double*)ptr = timestamp;
            ptr += sizeof(double);

            *(byte*)ptr = compressedAdditionalLatency;
            ptr += additionalLatencySize;

            //data.CopyTo(new Span<byte>(ptr, data.Length));
            fixed (byte* srcPtr = data)
            {
                Buffer.MemoryCopy(srcPtr, ptr, data.Length, data.Length);
            }

            return (byte*)pVoiceBuffer.ToPointer();
        }

        public unsafe void SendServerVoiceToClients(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data, int playerID)
        {
            int length = ExtraDataSize + data.Length;

            byte* pData = GetVoiceDataPointer(index, timestamp, additionalLatency, data);

            SendVoiceDataToClients(Sandbox, pData, ConnectionIdToPlayerObjectID[playerID], length);
        }

        public unsafe void SendVoiceDataToServer(int index, double timestamp, float additionalLatency, ReadOnlySpan<byte> data)
        {
            int totalLength = ExtraDataSize + data.Length;

            Sandbox.ConnectedServer.SendData(VoiceDataID, GetVoiceDataPointer(index, timestamp, additionalLatency, data), totalLength, TransportDeliveryMethod.Unreliable);
        }
    }
}
#endif