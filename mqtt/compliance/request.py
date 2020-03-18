import requests 
import json
import shutil
import time
import asyncio
import websockets

BASE = "https://localhost:3001/v1"

URL = f"{BASE}/conformance/cfg/mqtt/broker"

with open('config.json') as f:
  config = json.load(f)

result = requests.post(url = URL, data = config, verify=False)
print(result.text)

timestamp = result.json()['timestamp']
print(timestamp)

requests.get(url= f"{BASE}/conformance/run/mqtt/{timestamp}", verify=False)

async def wait_for_compleation():
    uri = f"ws://localhost:8080"
    async with websockets.connect(uri) as websocket:
        while(True):
            greeting = await websocket.recv()
            print(f"< {greeting}")

asyncio.get_event_loop().run_until_complete(wait_for_compleation())

print("Downloading")
response = requests.get(url= f"{BASE}/history/download/mqtt/{timestamp}", verify=False, stream=True)
with open(f"./result_{timestamp}.zip", "wb") as f:
    for block in response.iter_content(1024):
        f.write(block)

print(response)