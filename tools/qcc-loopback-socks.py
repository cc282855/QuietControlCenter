import socket

listener = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
listener.bind(("127.0.0.1", 19080))
listener.listen(8)
while True:
    connection, _ = listener.accept()
    with connection:
        try:
            hello = connection.recv(260)
            if hello and hello[0] == 5:
                connection.sendall(b"\x05\x00")
                request = connection.recv(512)
                if request:
                    connection.sendall(b"\x05\x07\x00\x01\x7f\x00\x00\x01\x00\x00")
        except OSError:
            pass
