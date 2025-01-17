using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;

public class NetworkManager : MonoBehaviour
{
    public static NetworkManager instance;

    public InputField ipInputField;
    public InputField portInputField;
    public InputField deviceIdInputField;
    public GameObject uiNotice;
    private TcpClient tcpClient;
    private NetworkStream stream;
    
    WaitForSecondsRealtime wait;

    private byte[] receiveBuffer = new byte[4096];
    private List<byte> incompleteData = new List<byte>();

    void Awake() {        
        instance = this;
        wait = new WaitForSecondsRealtime(5);
    }
    // 버튼 눌러서 게임 접속 시도
    public void OnStartButtonClicked() {
        string ip = ipInputField.text;
        string port = portInputField.text;

        if (IsValidPort(port)) {
            int portNumber = int.Parse(port);

            // deviceId 입력 혹은 미입력 시에 대한 처리
            if (deviceIdInputField.text != "") {
                GameManager.instance.deviceId = deviceIdInputField.text;
            } else {
                if (GameManager.instance.deviceId == "") {
                    GameManager.instance.deviceId = GenerateUniqueID();
                }
            }
  
            // 서버 접속 시도
            if (ConnectToServer(ip, portNumber)) {
                // 성공 시 시작
                StartGame();
            } else {
                // 서버 접속 실패 시 처리
                AudioManager.instance.PlaySfx(AudioManager.Sfx.LevelUp);
                // 1: connection failure 알림
                StartCoroutine(NoticeRoutine(1));
            }
            
        } else {
            // 유효하지 않은 포트일 시, 에러 음성과 노티스
            AudioManager.instance.PlaySfx(AudioManager.Sfx.LevelUp);
            StartCoroutine(NoticeRoutine(0));
        }
    }

    bool IsValidIP(string ip)
    {
        // 간단한 IP 유효성 검사
        return System.Net.IPAddress.TryParse(ip, out _);
    }

    // port 번호가 범위 내 유효한지에 대한 검사 (0 - 65535)
    bool IsValidPort(string port)
    {
        if (int.TryParse(port, out int portNumber))
        {
            return portNumber > 0 && portNumber <= 65535;
        }
        return false;
    }

    // 서버 접속 시도 
     bool ConnectToServer(string ip, int port) {
        try {
            tcpClient = new TcpClient(ip, port);
            stream = tcpClient.GetStream();
            Debug.Log($"Connected to {ip}:{port}");

            return true;
        } catch (SocketException e) {
            Debug.LogError($"SocketException: {e}");
            return false;
        }
    }

    // 고유 guid 생성
    string GenerateUniqueID() {
        return System.Guid.NewGuid().ToString();
    }

    // 정상적으로 접속시 게임 시작 처리
    void StartGame()
    {
        // 게임 시작 코드 작성
        Debug.Log("[StartGame] on StartGame");
        StartReceiving(); // Start receiving data
        SendInitialPacket();
    }

    // 알림 코루틴: 인덱스 별로 처리
    // 0: invalid address
    // 1: connection failure
    // 2: server error
    IEnumerator NoticeRoutine(int index) {
        
        uiNotice.SetActive(true);
        uiNotice.transform.GetChild(index).gameObject.SetActive(true);

        yield return wait;

        uiNotice.SetActive(false);
        uiNotice.transform.GetChild(index).gameObject.SetActive(false);
    }

    // byte stream에 대해 빅 엔디안으로 컨버팅
    public static byte[] ToBigEndian(byte[] bytes) {
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }
        return bytes;
    }

    // 패킷 헤더 생성
    byte[] CreatePacketHeader(int dataLength, Packets.PacketType packetType) {
        int packetLength = 4 + 1 + dataLength; // 전체 패킷 길이 (헤더 포함)
        byte[] header = new byte[5]; // 4바이트 길이 + 1바이트 타입

        // 첫 4바이트: 패킷 전체 길이
        byte[] lengthBytes = BitConverter.GetBytes(packetLength);
        lengthBytes = ToBigEndian(lengthBytes);
        Array.Copy(lengthBytes, 0, header, 0, 4);

        // 다음 1바이트: 패킷 타입
        header[4] = (byte)packetType;

        return header;
    }


    #region Packet Sending 
    //=======================================================================================================

    // 공통 패킷 생성 함수
    async void SendPacket<T>(T payload, uint handlerId)
    {
        // ArrayBufferWriter<byte>를 사용하여 직렬화
        var payloadWriter = new ArrayBufferWriter<byte>();
        Packets.Serialize(payloadWriter, payload);
        byte[] payloadData = payloadWriter.WrittenSpan.ToArray();

        CommonPacket commonPacket = new CommonPacket
        {
            handlerId = handlerId,
            //userId = GameManager.instance.deviceId,// 유저 아이디는 
            userId = GameManager.instance.userId,
            version = GameManager.instance.version,
            payload = payloadData,
        };

        // ArrayBufferWriter<byte>를 사용하여 직렬화
        var commonPacketWriter = new ArrayBufferWriter<byte>();
        Packets.Serialize(commonPacketWriter, commonPacket);
        byte[] data = commonPacketWriter.WrittenSpan.ToArray();

        // 헤더 생성
        byte[] header = CreatePacketHeader(data.Length, Packets.PacketType.Normal);

        // 패킷 생성
        byte[] packet = new byte[header.Length + data.Length];
        Array.Copy(header, 0, packet, 0, header.Length);
        Array.Copy(data, 0, packet, header.Length, data.Length);

        await Task.Delay(GameManager.instance.latency);
        
        // 패킷 전송
        stream.Write(packet, 0, packet.Length);
    }

    // 초기 패킷 전송
    void SendInitialPacket() {
        InitialPayload initialPayload = new InitialPayload
        {
            deviceId = GameManager.instance.deviceId,
            playerId = GameManager.instance.playerId,
            latency = GameManager.instance.latency,
        };

        // handlerId는 0으로 가정
        SendPacket(initialPayload, (uint)Packets.HandlerIds.Init);
    }

    // 위치 동기화 패킷의 전송
    public void SendLocationUpdatePacket(float x, float y) {
        LocationUpdatePayload locationUpdatePayload = new LocationUpdatePayload
        {
            gameId = GameManager.instance.gameId,
            x = x,
            y = y,
        };

        SendPacket(locationUpdatePayload, (uint)Packets.HandlerIds.LocationUpdate);
    }

    // 게임 생성 패킷 전송
    public void SendCreateGamePacket()
    {
        CreateGamePayload createGamePayload = new CreateGamePayload
        {
            timestamp = DateTime.Now,
        };

        SendPacket(createGamePayload, (uint)Packets.HandlerIds.CreateGame);
    }

    //=======================================================================================================
    #endregion

    // 패킷 수신 시작
    void StartReceiving() {
        _ = ReceivePacketsAsync();
    }

    // 비동기로 처리할 수신 패킷 처리
    async System.Threading.Tasks.Task ReceivePacketsAsync() {
        while (tcpClient.Connected) {
            try {
                int bytesRead = await stream.ReadAsync(receiveBuffer, 0, receiveBuffer.Length);
                if (bytesRead > 0) {
                    ProcessReceivedData(receiveBuffer, bytesRead);
                }
            } catch (Exception e) {
                Debug.LogError($"Receive error: {e.Message}");
                break;
            }
        }
    }

    void ProcessReceivedData(byte[] data, int length) {
         incompleteData.AddRange(data.AsSpan(0, length).ToArray());

        while (incompleteData.Count >= 5)
        {
            // 패킷 길이와 타입 읽기
            byte[] lengthBytes = incompleteData.GetRange(0, 4).ToArray();
            int packetLength = BitConverter.ToInt32(ToBigEndian(lengthBytes), 0);
            Packets.PacketType packetType = (Packets.PacketType)incompleteData[4];

            if (incompleteData.Count < packetLength)
            {
                // 데이터가 충분하지 않으면 반환
                return;
            }

            // 패킷 데이터 추출
            byte[] packetData = incompleteData.GetRange(5, packetLength - 5).ToArray();
            incompleteData.RemoveRange(0, packetLength);

            // Debug.Log($"Received packet: Length = {packetLength}, Type = {packetType}");

            switch (packetType)
            {
                case Packets.PacketType.Normal:
                    HandleNormalPacket(packetData);
                    break;
                case Packets.PacketType.Location:
                    HandleLocationPacket(packetData);
                    break;
                case Packets.PacketType.Ping:
                    HandlePingPacket(packetData);
                    break;
            }
        }
    }

    void HandleNormalPacket(byte[] packetData) {
        // 패킷 데이터 처리
        var response = Packets.Deserialize<Response>(packetData);
        Debug.Log($"HandlerId: {response.handlerId}, responseCode: {response.responseCode}, timestamp: {response.timestamp}");
        
        // 서버로부터 온 응답코드가 잘못됨
        if (response.responseCode != 0 && !uiNotice.activeSelf) {
            AudioManager.instance.PlaySfx(AudioManager.Sfx.LevelUp);
            // 2: server error 알림
            StartCoroutine(NoticeRoutine(2));
            return;
        }

        // 응답 데이터가 유효하게 존재하면
        if (response.data != null && response.data.Length > 0) {
            // init 패킷 응답
            if (response.handlerId == 0) {
                //GameManager.instance.GameStart();
                string userId = ParseResponseData(response.data, "userId");
                Debug.Log("[InitialResponse] UserId: " + userId);
                // userId 값 받아와서 setter
                GameManager.instance.SetUserId(userId);
                // 게임 생성 패킷 전송
                SendCreateGamePacket();
            }
            // CreateGame 응답
            if (response.handlerId == 4)
            {
                string gameId = ParseResponseData(response.data, "gameId");
                Debug.Log("[CreateGameResponse] gameId: " + gameId);
                // gameId 값 받아와서 setter
                GameManager.instance.SetGameId(gameId);
                //
                GameManager.instance.GameStart();
            }
            ProcessResponseData(response.data);
        }
    }

    void ProcessResponseData(byte[] data) {
        try {
            // var specificData = Packets.Deserialize<SpecificDataType>(data);
            string jsonString = Encoding.UTF8.GetString(data);
            Debug.Log($"Processed SpecificDataType: {jsonString}");
        } catch (Exception e) {
            Debug.LogError($"Error processing response data: {e.Message}");
        }
    }


    string ParseResponseData(byte[] data, string targetKey)
    {
        try
        {
            // 바이트 배열을 문자열로 변환
            string jsonString = Encoding.UTF8.GetString(data);

            // 특정 키를 검색
            string keyPattern = $"\"{targetKey}\":";
            int keyIndex = jsonString.IndexOf(keyPattern);

            if (keyIndex == -1)
            {
                Debug.LogWarning($"Key '{targetKey}' not found in response data.");
                return null;
            }

            // 값의 시작 위치 계산
            int valueStartIndex = keyIndex + keyPattern.Length;

            // 값이 따옴표로 묶여 있는 경우 처리
            bool isQuoted = jsonString[valueStartIndex] == '\"';
            if (isQuoted) valueStartIndex++;

            // 값의 끝 위치 찾기
            int valueEndIndex = isQuoted
                ? jsonString.IndexOf('\"', valueStartIndex)  // 따옴표로 묶인 값
                : jsonString.IndexOfAny(new char[] { ',', '}' }, valueStartIndex);  // 숫자 등 단순 값

            if (valueEndIndex == -1)
            {
                Debug.LogWarning($"Value for key '{targetKey}' could not be determined.");
                return null;
            }

            // 값 추출 및 반환
            string value = jsonString.Substring(valueStartIndex, valueEndIndex - valueStartIndex);
            return value;
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing response data: {e.Message}");
            return null;
        }
    }

    void HandleLocationPacket(byte[] data) {
        try {
            //Debug.Log("[HandleLocationPacket] called, data.Length : " + data.Length);
            //Debug.Log("[HandleLocationPacket] is null? : " + (data == null));
            LocationUpdate response = Packets.Deserialize<LocationUpdate>(data);
            Debug.Log("is response null? : "+ (response == null));
            Debug.Log("response.users?.Count: " + response.users?.Count);

            if (response.users.Count > 0)
            {
                // 각 사용자에 대해 위치 처리
                foreach (var user in response.users)
                {
                    Debug.Log($"[HandleLocationPacket] id: {user.id} / playerId: {user.playerId} / x: {user.x} / y: {user.y}");
                }
            }
            var tmp = new LocationUpdate { users = new List<LocationUpdate.UserLocation>() };

            // 사용자 위치 정보 스폰
            Spawner.instance.Spawn(data.Length > 0 ? response : tmp);
            /*
            #region LEGACY HANDLELOCATIONPACKET
            //Debug.Log("[HandleLocationPacket] called, data.Length : " + data.Length);
            //Debug.Log("[HandleLocationPacket] is null? : " + (data == null));
            LocationUpdate response;

            if (data.Length > 0)
            {
                // 패킷 데이터 처리
                response = Packets.Deserialize<LocationUpdate>(data);
            }
            else
            {
                // data가 비어있을 경우 빈 배열을 전달
                response = new LocationUpdate { users = new List<LocationUpdate.UserLocation>() };
            }

            // 현재 이부분에 인덱스 에러
            Debug.Log($"data.users count: {response.users?.Count}");

            Spawner.instance.Spawn(response);
            #endregion
            */
        }
        catch (Exception e) {
            Debug.LogError($"Error HandleLocationPacket: {e.Message}");
        }
    }

    // Ping 패킷 처리
    void HandlePingPacket(byte[] data)
    {
        Debug.Log("[Ping] ");
    }
}
