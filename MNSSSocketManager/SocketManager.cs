using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Data.SqlClient;
using System.Data;
using Common;
using System.Configuration;
using System.Collections.Generic;

namespace MNSSSocketManager {
    public class SocketManager {
        private Dictionary<int, StateObject> mChats = new Dictionary<int, StateObject>(); 
        private int mPort;
        private string mHostName=null;
        private Socket mListener = null;
        // Thread signal.
        public ManualResetEvent mAllDone = new ManualResetEvent(false);
        public enum RESULT_STATUS { SUCCESS = 0, FAIL };
        private static String COMMAND_HERES_MY_CHAT_MSG = "_TRANSACTION_heresmychatmsg";
        private static String COMMAND_ACK = "_TRANSACTION_ack";
        private static String COMMAND_IAM = "_TRANSACTION_IAm";
        private static String COMMAND = "_TRANSACTION_";
        private ReceivesSocketManagerMessages mRsmm;
        private string mConnectionString;

        public SocketManager(int port,ReceivesSocketManagerMessages rsmm, string connectionString) {
            mPort = port;
            mRsmm=rsmm;
            mConnectionString = connectionString;
        }
        public SocketManager(int port, string hostName, ReceivesSocketManagerMessages rsmm, string connectionString) {
			mHostName=hostName;
            mRsmm=rsmm;
            mPort = port;
            mConnectionString = connectionString;
        }
        public void Start() {
            ThreadStart ts = new ThreadStart(InternalStartListening);
            Thread thread = new Thread(ts);
            thread.Start();
        }
        public void Stop() {
            try {
                mListener.Close();
            }
            catch { }
        }
        private void InternalStartListening() {
            // Data buffer for incoming data.	
            byte[] bytes = new Byte[1024];

            // Establish the local endpoint for the socket.
            // The DNS name of the computer
            // running the listener is "host.contoso.com".
            IPHostEntry ipHostInfo = null;
            if (mHostName == null) {
                ipHostInfo = Dns.Resolve(Dns.GetHostName());
            }
            else {
                ipHostInfo = Dns.GetHostByName(mHostName);
            }
            IPAddress ipAddress = ipHostInfo.AddressList[0];
            mRsmm.heresMyPort(mPort);
            mRsmm.heresMyServer(ipAddress.ToString());

            IPEndPoint localEndPoint = new IPEndPoint(ipAddress, mPort);

            // Create a TCP/IP socket.
            mListener = new Socket(AddressFamily.InterNetwork,
                SocketType.Stream, ProtocolType.Tcp);
            // Bind the socket to the local endpoint and listen for incoming connections.
            try {
                mListener.Bind(localEndPoint);
                mListener.Listen(100);

                while (true) {
                    // Set the event to nonsignaled state.
                    mAllDone.Reset();

                    // Start an asynchronous socket to listen for connections.
                    mListener.BeginAccept(
                        new AsyncCallback(AcceptCallback),
                        mListener);

                    // Wait until a connection is made before continuing.
                    mAllDone.WaitOne();
                }

            }
            catch (Exception e) {
                if (!listenerWasClosed(e)) {
                    throw e;
                }
            }
        }
        private void AcceptCallback(IAsyncResult ar) {
            // Signal the main thread to continue.
            try {
                mAllDone.Set();

                // Get the socket that handles the client request.
                Socket socketListener = (Socket)ar.AsyncState;
                Socket socketHandler = mListener.EndAccept(ar);

                // Create the state object.
                StateObject state = new StateObject();
                state.workSocket = socketHandler;
                socketHandler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
            }   
            catch { }
        }
        private bool listenerWasClosed(Exception e) {
            return e.Message.ToLower().IndexOf("cannot access a disposed object") != -1;
        }
        private void ReadCallback(IAsyncResult ar) {
            String content = String.Empty;

            // Retrieve the state object and the handler socket
            // from the asynchronous state object.
            StateObject state = (StateObject)ar.AsyncState;
            Socket socketHandler = state.workSocket;
            bool readARecord = false;

            // Read data from the client socket. 
            int bytesRead = socketHandler.EndReceive(ar);

            if (bytesRead > 0) {
                // There  might be more data, so store the data received so far.
                state.sb.Append(Encoding.ASCII.GetString(
                    state.buffer, 0, bytesRead));

                // Check for end-of-file tag. If it is not there, read 
                // more data.
                RESULT_STATUS resultStatus = RESULT_STATUS.SUCCESS;
                content = state.sb.ToString();
                if (content.IndexOf(COMMAND) > -1) { // need to change this so that at e-o-transmission we send something distinct
                    state.sb = new StringBuilder();
                    // All the data has been read from the 
                    // client. Do something with it.
                    content = content.Replace("\r\n", "");
                    readARecord = true;
                    Communique com = Communique.buildCommunique(content);
                    if (com is CommuniqueMessage) {
                        state.mMyUserId = com.mFromUserId;
                        state.mName = com.mFromName;
                        state.mPartnerUserId = com.mToUserId;
                        state.mPartnerName = com.mToName;
                        if (mChats.ContainsKey(com.mToUserId)) {
                            state.mPartnerSocket = mChats[com.mToUserId].workSocket;
                            Send(mChats[com.mToUserId].workSocket, com.ToString());
                        }
                        ((CommuniqueMessage)com).doACK(state.workSocket);
                        if(!mChats.ContainsKey(com.mFromUserId)) {
                            mChats[com.mFromUserId] = state;
                        }
                    } else {
                        if (com is CommuniqueACK) {
                            if (mChats.ContainsKey(com.mToUserId)) {
                                state.mPartnerSocket = mChats[com.mToUserId].workSocket;
                                Send(mChats[com.mToUserId].workSocket, com.ToString());
                            }
                        } else {
                            if (com is CommuniqueIAm) {
                                state.mMyUserId = com.mFromUserId;
                                state.mName = com.mFromName;
                                mChats[com.mFromUserId] = state;
                                SqlCommand cmd = new SqlCommand("uspSetPortAndURL");
                                cmd.Parameters.Add("@Port", SqlDbType.Int).Value = null;
                                cmd.Parameters.Add("@URL", SqlDbType.VarChar).Value = null;
                                cmd.Parameters.Add("@UserId", SqlDbType.Int).Value = com.mFromUserId;
                                cmd.Parameters.Add("@IsOnline", SqlDbType.Int).Value = 1;
                                Utils.executeNonQuery(cmd, ConnectionString);
                                com.doACK(state.workSocket);

                            }
                        }
                    }
                    mRsmm.iReceivedThisDatums(content);
                }
                else {
                    // Not all data received. Get more.
                    socketHandler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReadCallback), state);
                }
                if (readARecord) {
                    readARecord = false;
                }
                socketHandler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                    new AsyncCallback(ReadCallback), state);
            }
        }
        private void Send(Socket handler, String data) {
            // Convert the string data to byte data using ASCII encoding.
            data += "\r\n";
            byte[] byteData = Encoding.ASCII.GetBytes(data);

            // Begin sending the data to the remote device.
            handler.BeginSend(byteData, 0, byteData.Length, 0,
                new AsyncCallback(SendCallback), handler);
        }

        private void SendCallback(IAsyncResult ar) {
            try {
                // Retrieve the socket from the state object.
                Socket handler = (Socket)ar.AsyncState;

                // Complete sending the data to the remote device.
                int bytesSent = handler.EndSend(ar);
                //				Console.WriteLine("Sent {0} bytes to client.", bytesSent);

                
            //    handler.Shutdown(SocketShutdown.Both);
              //  handler.Close();

            }
            catch (Exception e) {
                throw e;
            }
        }
        private string ConnectionString {
            get {
                ConnectionStringSettings settings =
                    ConfigurationManager.ConnectionStrings[ConfigurationManager.AppSettings["MassageNearby"]];
                return settings.ConnectionString;
            }
        }

        // State object for reading client data asynchronously
        private class StateObject {
            // Client  socket.
            public Socket workSocket = null;
            // Size of receive buffer.
            public const int BufferSize = 1024;
            // Receive buffer.
            public byte[] buffer = new byte[BufferSize];
            // Received data string.
            public StringBuilder sb = new StringBuilder();
            // My UserId
            public int mMyUserId;
            // Partner UserId
            public int mPartnerUserId;
            // My name
            public string mName;
            // Partner name
            public string mPartnerName;
            // Partner socket
            public Socket mPartnerSocket;
        }
        public abstract class Communique {
            public int mFromUserId;
            public int mToUserId;
            public String mFromName;
            public String mToName;
            protected abstract string commandType { get; }
            public static Communique buildCommunique(string communique) {
                String[] msgComponents = communique.Split(new char[] { '~' });
                string name = msgComponents[0];
                int id = Convert.ToInt32(msgComponents[1]);
                string partnerName = msgComponents[2];
                int partnerId = 0;
                try {
                    partnerId = Convert.ToInt32(msgComponents[3]);
                }
                catch { }
                string transactionType = msgComponents[4];
                if (transactionType == COMMAND_HERES_MY_CHAT_MSG) {
                    return new CommuniqueMessage(id, partnerId, name, partnerName, msgComponents[5]);
                }
                else {
                    if (transactionType == COMMAND_ACK) {
                        return new CommuniqueACK(id, partnerId, name, partnerName);
                    }
                    else {
                        if (transactionType == COMMAND_IAM) {
                            return new CommuniqueIAm (name,id);
                        }
                        else {
                            return null;
                        }
                    }
                }
            }
            public Communique(int fromUserId, int toUserId, string fromName, string toName) {
                mFromName = fromName;
                mFromUserId = fromUserId;
                mToName = toName;
                mToUserId = toUserId;
            }
            public void doACK(Socket socket) {
                string data = new CommuniqueACK(mFromUserId, mToUserId, mFromName, mToName).ToString();
                data += "\r\n";

                byte[] byteData = Encoding.ASCII.GetBytes(data);
                socket.Send(byteData);
            }

            public override string ToString() {
                return "" + mFromName + "~" + mFromUserId + "~" + mToName + "~" + mToUserId + "~" + commandType;
            }
        }
        public class CommuniqueIAm : Communique {
            public CommuniqueIAm(string fromName, int fromUserId)
                : base(
                    fromUserId, 0, fromName, null) {
            }
            protected override string commandType {
                get { return COMMAND_IAM; }
            }
        }
        public class CommuniqueMessage : Communique {
            string mMessage;
            public CommuniqueMessage(int fromUserId, int toUserId, string fromName, string toName, string message)
                : base(
                    fromUserId, toUserId, fromName, toName) {
                mMessage = message;
            }
            protected override string commandType {
                get { return COMMAND_HERES_MY_CHAT_MSG+"~"+mMessage; }
            }
        }
        public class CommuniqueACK : Communique {
            public CommuniqueACK(int fromUserId, int toUserId, string fromName, string toName)
                : base(
                    fromUserId, toUserId, fromName, toName) {
            }
            protected override string commandType {
                get { return COMMAND_ACK; }
            }
        }

    }

}
