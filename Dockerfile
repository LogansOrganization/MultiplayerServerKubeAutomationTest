FROM ubuntu:22.04

WORKDIR /server

RUN apt-get update && \
    apt-get install -y libstdc++6 && \
    rm -rf /var/lib/apt/lists/*

COPY buildServer/ .

RUN chmod +x *.x86_64

EXPOSE 7777/udp

ENTRYPOINT ["./KubeServerTest.x86_64", "-batchmode", "-nographics"]