namespace BACKRabbit.Protocol.Firehose;

public enum SaharaCommand : uint
{
    HelloReq            = 0x00,
    HelloRsp            = 0x01,
    ReadData            = 0x02,
    EndOfImageTransfer  = 0x03,
    DoneReq             = 0x04,
    DoneRsp             = 0x05,
    ClientCmdReq        = 0x06,
    ClientCmdRsp        = 0x07,
    MemoryDebugReq      = 0x08,
    MemoryDebugRsp      = 0x09,
}

public enum SaharaMode : uint
{
    ImageTxPending   = 0x00,
    ImageTxComplete  = 0x01,
    MemoryDebug      = 0x02,
    Command          = 0x03,
}

public enum SaharaError : uint
{
    Success                     = 0x00,
    InvalidCommand              = 0x01,
    ProtocolMismatch            = 0x02,
    InvalidTargetProtocol       = 0x03,
    InvalidHostProtocol         = 0x04,
    InvalidPacketSize           = 0x05,
    UnexpectedImageId           = 0x06,
    InvalidImageHeaderSize      = 0x07,
    InvalidImageHeader          = 0x08,
    InvalidImageType            = 0x09,
    InvalidImageLength          = 0x0A,
    InvalidImageDest            = 0x0B,
    GeneralTxRxError            = 0x0C,
    ReadDataError               = 0x0D,
    UnsupportedNumImages        = 0x0E,
    InvalidImageCert            = 0x0F,
    InvalidImageElfFormat       = 0x10,
    CommandExecFailure          = 0x1C,
    CommandInvalidParam         = 0x1D,
    MemoryDebugNotSupported     = 0x1E,
}
