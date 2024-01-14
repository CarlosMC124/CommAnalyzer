using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Transactions;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Shapes;
using Xceed.Wpf.AvalonDock.Themes;

namespace CommAnalyzer.Helpers.DNP3
{
    public class DNP3Support
    {
        public static void GeneralCRC(byte[] message, ref byte[] CRC, long poly_CRC, long init_value, long exit_value, long XOROUT)
        {
            long init_loop = init_value;

            for (int i = 0; i < (message.Length); i++)
            {
                long crcByte = message[i];
                crcByte = init_loop ^ crcByte;

                for (int j = 0; j < 8; j++)
                {
                    if ((crcByte & 0x0001) == 1)
                    {
                        crcByte = (crcByte >> 1);
                        crcByte = (crcByte ^ poly_CRC);   //3D65 (espejo)
                    }
                    else
                        crcByte = (crcByte >> 1);
                }
                crcByte ^= exit_value;  //Si es 0x0000, no se cambia nada
                init_loop = crcByte;
            }
            init_loop ^= XOROUT;

            CRC[1] = (byte)((init_loop >> 8) & 0xFF);
            CRC[0] = (byte)(init_loop & 0xFF);
        }
    }

    public class DNP3Message
    {
        /*
            2Bytes		   1Byte	1Byte	   2Bytes		   2Bytes		   2Bytes
         --------------------------------------------------------------------------------
        |  START BYTES   |   LN  |  DLC  |  DEST  FIELD  |	SOURC  FIELD |	    CRC      |
         --------------------------------------------------------------------------------
        |   1	 |   2	 |   3	 |   4	 |   5	 |   6	 |   7	 |   8	 |   9	 |  10	 |
         --------------------------------------------------------------------------------
        |   05	 |   64	 |   	

       */

        public DNP3Message(byte[] byteMessage)
        {
            Array.Copy(byteMessage, 0, StartMessage, 0, 2);
            LN = byteMessage[2];
            _DLC = byteMessage[3];
            DLC = _DLC;
            Array.Copy(byteMessage, 4, DestField, 0, 2);
            Array.Copy(byteMessage, 6, SourField, 0, 2);
            Array.Copy(byteMessage, 8, CRC, 0, 2);

            byte[] arrayPCRC = new byte[8];
            byte[] calcCRC = new byte[2];
            Array.Copy(byteMessage, 0, arrayPCRC, 0, 8);
            DNP3Support.GeneralCRC(arrayPCRC, ref calcCRC, 0xA6BC, 0x0000, 0x0000, 0xFFFF);

            CRCok = CRC.SequenceEqual(calcCRC); //(CRC == calcCRC); No funciona
        }

        public byte[] StartMessage = new byte[2];
        public byte LN = 0;

        /*
         ----------------------------------------------------------------
        |  DIR   |  PRM  |  FCB  |  FCV  |      Primary Func Code        |	
        |   	 |   	 |   0	 |  DFC  |     Secondary Func Code	     |
         ----------------------------------------------------------------
        |   7	 |   6	 |   5	 |   4	 |   3	 |   2	 |   1	 |   0	 |
       */
        private byte _DLC = 0;
        public bool DLC_DIR = false;   //The Direction bit is independent from the Data Link Layer transmission originating from the Primary
                                //Station or from the Secondary Station.The transmitting device shall set the Direction bit to indicate the
                                //physical transmission origin of the data link frame.
                                //⎯ DIR = 1 indicates a frame from a Master.
                                //⎯ DIR = 0 indicates a frame from an Outstation.

        bool DLC_PRM = false;   //The Primary Message Bit indicates the direction of the data link frame with respect to the initiating station.
                                //⎯ PRM = 1 indicates a Data Link Layer transaction is being initiated by either a master or an outstation,
                                //and the function code is chosen from Table 9-1. The FCB and FCV fields function in their Data
                                //Link Layer synchronization mode as described in 9.2.4.1.3.3 and 9.2.4.1.3.4.
                                //⎯ PRM = 0 indicates a Data Link Layer transaction is being completed by either a master or an
                                //outstation, and the function code is chosen from Table 9-2. Bit 5 shall always be 0. The DFC field
                                //(bit 4) functions in its Data Link Layer flow control mode as described in 9.2.4.1.3.5.

        bool DLC_FCB = false;   //The FCB and FCV bit fields function together to maintain synchronization for Confirmed User Data
                                //services.Typically devices should use Unconfirmed User Data services.
                                //The Frame Count bit (FCB) is used to detect losses and duplication in primary-to-secondary frames. The
                                //Data Link Layer in a Secondary Station can detect that a request is new when it receives a frame having the
                                //FCV bit set and an FCB that matches the state expected by the Secondary Station.When the Secondary
                                //Station detects this condition, it toggles the state that it expects for the FCB in the next message with the
                                //FCV bit set. If the received FCB bit in a message does not match the expected FCB state when the FCV is
                                //set, the Secondary Station recognizes the message as being a repeat of a previous message and acts
                                //according to 9.2.6.3

        bool DLC_FCV = false;   //The Frame Count Valid(FCV) bit appears in every primary-to-secondary frame and specifies whether the
                                //Secondary Station is to examine the FCB bit.Table 9-1 indicates the usage of the FCV bit.
                                //⎯ FCV = 1 indicates the state of the FCB bit is valid and that the state of the FCB bit in the received
                                //message shall be checked against its expected state.
                                //⎯ FCV = 0 indicates the state of the FCB bit is ignored.
                                //Before a station acting as a Primary Station can transmit primary-to-secondary frames with the FCV bit set,
                                //it shall first verify that the Secondary Station’s expected FCB (EFCB) is properly synchronized. The
                                //Primary Station does this by sending a request to reset the Secondary Station’s link states.It should send
                                //this message upon detection of communication loss or whenever either station restarts.
                                //Each Secondary Station, after Data Link Layer startup or transaction failure, shall not accept any Primary
                                //Station request messages having the FCV bit set until its FCB state has been reset by receiving a reset link
                                //states request from the Primary Station.


        bool DLC_DFC = false;   //The Data Flow Control(DFC) bit appears in every response from a Secondary Station regardless of the
                                //function code in the control octet.It is used to report an insufficient number of Data Link Layer buffers to
                                //hold a receive frame.It is also used to indicate that the Secondary Station’s Data Link Layer is busy.
                                //⎯ DFC = 1 indicates receive buffers were not available or that the Secondary Station’s Data Link
                                //Layer was busy.
                                //⎯ DFC = 0 indicates receive buffers were available and the Secondary Station’s Data Link Layer
                                //was ready.

        int DLC_FunctionCode = 0;   //FUNCTION CODE field
        //The Function Code identifies the function or service associated with the data link frame.The definition of
        //the values placed in this field depends on whether Data Link Layer messages are sent from the Primary
        //Station to the Secondary Station or from the Secondary Station to the Primary Station.Table 9-1 and
        //Table 9-2 specify the codes and associated FCV states.A detailed description of each function is provided
        //in 9.2.6 and 9.2.7.

        //        Table 9-1—Primary-to-secondary(PRM = 1) function codes
        // Primary          Function                Service                     FCV         Response function
        // function code    code name               function                    bit         codes permitted from Secondary Station
        //-------------------------------------------------------------------------------------------------------------------------------------------------
        //  0               RESET_LINK_STATES       Reset of remote link        0           0 or 1
        //  1               —                       Obsolete                    —           15 or no response
        //  2               TEST_LINK_STATES        Test function for link      1           0 or 1 (no response is acceptable if the link states are UnReset)
        //  3               CONFIRMED_USER_DATA     Deliver application data,   1           0 or 1
        //                                          confirmation requested      
        //  4               UNCONFIRMED_USER_DATA   Deliver application data,   0           No Secondary Station
        //                                          confirmation not requested              Data Link response
        //  5               —                       Reserved                    —           15 or no response
        //  6               —                       Reserved                    —           15 or no response
        //  7               —                       Reserved                    —           15 or no response
        //  8               —                       Reserved                    —           15 or no response
        //  9               REQUEST_LINK_STATUS     Request status of link      0           11
        //  10              —                       Reserved                    —           15 or no response
        //  11              —                       Reserved                    —           15 or no response
        //  12              —                       Reserved                    —           15 or no response
        //  13              —                       Reserved                    —           15 or no response
        //  14              —                       Reserved                    —           15 or no response
        //  15              —                       Reserved                    —           15 or no response


        //          Table 9-2—Secondary-to-primary(PRM = 0) function codes
        //  Secondary           Function code           Service 
        //  function code       name                    function
        //-------------------------------------------------------------------
        //  0                   ACK                     Positive acknowledgment
        //  1                   NACK                    Negative acknowledgment
        //  2                   —                       Reserved
        //  3                   —                       Reserved
        //  4                   —                       Reserved
        //  5                   —                       Reserved
        //  6                   —                       Reserved
        //  7                   —                       Reserved
        //  8                   —                       Reserved
        //  9                   —                       Reserved
        //  10                  —                       Reserved
        //  11                  LINK_STATUS             Status of the link
        //  12                  —                       Reserved
        //  13                  —                       Reserved
        //  14                  —                       Obsolete
        //  15                  NOT_SUPPORTED           Link service not supported


        public byte DLC { 
            get { return _DLC; }
            set {
                DLC_DIR = (value & 0x80) != 0;
                DLC_PRM = (value & 0x40) != 0;

                if(DLC_DIR)
                {
                    DLC_FCB = (value & 0x20) != 0;
                    DLC_FCV = (value & 0x10) != 0;
                }
                else
                {
                    DLC_DFC = (value & 0x10) != 0;
                }

                DLC_FunctionCode = (value & 0x0F);
            }
        }
       

       

        public string DLCString() {
            string DLCState = string.Empty;

            //SECONDARY | DFC = 0
            if(DLC == 0x00 || DLC == 0x80)
            {
                DLCState += "ACK | ";
            }
            else if (DLC == 0x01 || DLC == 0x81)
            {
                DLCState += "NACK (Failed Transaction) | ";
            }
            else if (DLC == 0x0B || DLC == 0x8B)
            {
                DLCState += "Link Status Reply (No Flow Control) | ";
            }
            else if (DLC == 0x0F || DLC == 0x8F)
            {
                DLCState += "Not Supported (Link services not supported) | ";
            }
            //SECONDARY | DFC = 1
            else if (DLC == 0x10 || DLC == 0x90)
            {
                DLCState += "ACK (No buffer, frame accepted) | ";
            }
            else if (DLC == 0x11 || DLC == 0x91)
            {
                DLCState += "NACK (No buffer, frame not accepted) | ";
            }
            else if (DLC == 0x1B || DLC == 0x9B)
            {
                DLCState += "Link Status Reply (No buffer) | ";
            }
            else if (DLC == 0x1F || DLC == 0x9F)
            {
                DLCState += "Not Supported (No buffer, Link services not supported) | ";
            }
            //PRIMARY | FCV = 0
            else if (DLC == 0x40 || DLC == 0xC0)
            {
                DLCState += "RESET LINK (FCB=0 Ignored) | ";
            }
            else if (DLC == 0x41 || DLC == 0xC1)
            {
                DLCState += "Reset User Process (Obsolete) | ";
            }
            else if (DLC == 0x44 || DLC == 0xC4)
            {
                DLCState += "Unconfirmed user data (FCB=0 Ignored) | ";
            }
            else if (DLC == 0x49 || DLC == 0xC9)
            {
                DLCState += "Link status request (FCB=0 Ignored) | ";
            }
            else if (DLC == 0x60 || DLC == 0xE0)
            {
                DLCState += "RESET LINK (FCB=1 Ignored) | ";
            }
            else if (DLC == 0x61 || DLC == 0xE1)
            {
                DLCState += "Reset User Process (Obsolete) | ";
            }
            else if (DLC == 0x64 || DLC == 0xE4)
            {
                DLCState += "Unconfirmed user data (FCB=1 Ignored) | ";
            }
            else if (DLC == 0x69 || DLC == 0xE9)
            {
                DLCState += "Link status request (FCB=1 Ignored) | ";
            }
            //PRIMARY | FCV = 1
            else if (DLC == 0x52 || DLC == 0xD2)
            {
                DLCState += "TEST LINK (FCB=0) | ";
            }
            else if (DLC == 0x53 || DLC == 0xD3)
            {
                DLCState += "Confirmed user data (FCB=0) | ";
            }
            else if (DLC == 0x72 || DLC == 0xE2)
            {
                DLCState += "TEST LINK (FCB=1) | ";
            }
            else if (DLC == 0x73 || DLC == 0xE3)
            {
                DLCState += "Confirmed user data (FCB=1) | ";
            }

            DLCState += DLC_DIR ? "DIR = 1 (from a Master.) | " : "DIR = 0 (from an Outstation.) | ";
            DLCState += DLC_PRM ? "PRM = 1 (transaction is being initiated.) | " : "PRM = 0 (transaction is being completed.) | ";
            if (DLC_DIR)
            {
                DLCState += DLC_FCB? "FCB = 1 | " : "FCB = 0 | ";
                DLCState += DLC_FCV? "FCV = 1 (FCB bit is valid.) | " : "FCV = 0 (FCB bit is ignored.) | ";
            }
            else
            {
                //⎯ DFC = 1 indicates receive buffers were not available or that the Secondary Station’s Data Link
                //Layer was busy.
                //⎯ DFC = 0 indicates receive buffers were available and the Secondary Station’s Data Link Layer
                DLCState += DLC_DFC ? "DFC = 1 (buffers were not available.) | " : "DFC = 0 (buffers were available.) | ";
            }

            DLCState += "Function Code =" + DLC_FunctionCode;

            return DLCState;
        }

        public byte[] DestField = new byte[2];
        public byte[] SourField = new byte[2];
        public byte[] CRC = new byte[2];
        public bool CRCok = false;
    }
}
