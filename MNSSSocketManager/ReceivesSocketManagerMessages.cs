using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MNSSSocketManager {
    public interface ReceivesSocketManagerMessages {
        void heresMyPort(int port);
        void heresMyServer(String server);
        void iReceivedThisDatums(String datums);
    }
}
