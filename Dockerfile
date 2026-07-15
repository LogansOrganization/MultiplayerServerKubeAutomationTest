FROM ubuntu:22.04

WORKDIR /server

RUN apt-get update && \
    apt-get install -y libstdc++6 && \
    rm -rf /var/lib/apt/lists/*

COPY buildServer/ .

RUN chmod +x Server

EXPOSE 7777/udp

ENTRYPOINT ["./Server", "-batchmode", "-nographics"]