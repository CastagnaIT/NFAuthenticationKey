# -*- coding: utf-8 -*-
"""
    Copyright (C) 2020 Stefano Gottardo
    SPDX-License-Identifier: GPL-3.0-only
    See LICENSE.md for more information.
"""
import base64
import json
import os
import platform
import random
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from datetime import datetime, timedelta

import websocket  # pip install websocket-client

try:  # Python 3
    from urllib.request import HTTPError, URLError, urlopen
except ImportError:  # Python 2
    from urllib2 import HTTPError, URLError, urlopen

try:  # The crypto package depends on the package installed
    from Cryptodome.Cipher import AES
    from Cryptodome.Util import Padding
except ImportError:
    from Crypto.Cipher import AES
    from Crypto.Util import Padding

IS_MACOS = platform.system().lower() == 'darwin'

# Script configuration
BROWSER_PATH = '* Remove me and specify here the browser path, only if not recognized *'
DEBUG_PORT = 9222
LOCALHOST_ADDRESS = 'localhost'
URL = 'https://www.netflix.com/login'


class Main(object):

    app_version = '1.1.1'
    _msg_id = 0
    _ws = None

    def __init__(self, browser_temp_path):
        show_msg('')
        show_msg(TextFormat.BOLD + 'NFAuthentication Key for Linux/MacOS (Version {})'.format(self.app_version),
                 TextFormat.COL_LIGHT_BLUE)
        show_msg('')
        show_msg('Disclaimer:')
        show_msg('This script and source code available on GitHub are provided "as is" without warranty of any kind, either express or implied. Use at your own risk. The use of the software is done at your own discretion and risk with the agreement that you will be solely responsible for any damage resulting from such activities and you are solely responsible for adequate data protection.',
                 TextFormat.COL_GREEN)
        show_msg('')
        browser_proc = None
        try:
            input_msg('Press "ENTER" key to accept the disclaimer and start, or "CTRL+C" to cancel', TextFormat.BOLD)
            show_msg('Browser startup... please wait')
            browser_proc = open_browser(browser_temp_path)
            self.operations()
        except Warning as exc:
            show_msg(str(exc), TextFormat.COL_LIGHT_RED)
            if browser_proc:
                browser_proc.terminate()
        except Exception as exc:
            show_msg('An error is occurred:\r\n' + str(exc), TextFormat.COL_LIGHT_RED)
            import traceback
            show_msg(traceback.format_exc())
            if browser_proc:
                browser_proc.terminate()
        finally:
            try:
                if self._ws:
                    self._ws.close()
            except Exception:
                pass

    def operations(self):
        show_msg('Establish connection with the browser... please wait')
        self.get_browser_debug_endpoint()
        self.ws_request('Network.enable')
        self.ws_request('Page.enable')
        show_msg('Opening login webpage... please wait')
        self.ws_request('Page.navigate', {'url': URL})

        self.ws_wait_event('Page.domContentEventFired')  # Wait loading DOM (document.onDOMContentLoaded event)

        show_msg('Please login in to website now ...waiting for you to finish...', TextFormat.COL_LIGHT_BLUE)
        if not self.wait_user_logged():
            raise Warning('You have exceeded the time available for the login. Restart the operations.')

        self.ws_wait_event('Page.domContentEventFired')  # Wait loading DOM (document.onDOMContentLoaded event)

        # Verify that falcorCache data exist, this data exist only when logged
        show_msg('Verification of data in progress... please wait')
        html_page = self.ws_request('Runtime.evaluate', {'expression': 'document.documentElement.outerHTML'})['result']['value']
        if 'falcorCache' not in html_page:
            raise Warning('Possible wrong login or unexpected problem, please try again.')

        self.ws_wait_event('Page.loadEventFired')  # Wait loading page (window.onload event)

        show_msg('File creation in progress... please wait')
        # Get all cookies
        cookies = self.ws_request('Network.getAllCookies').get('cookies', [])
        assert_cookies(cookies)
        # Generate a random PIN for access to "NFAuthentication.key" file
        pin = random.randint(1000, 9999)
        # Create file data structure
        data = {
            'app_name': 'NFAuthenticationKey',
            'app_version': self.app_version,
            'app_system': 'MacOS' if IS_MACOS else 'Linux',
            'app_author': 'CastagnaIT',
            'timestamp': int(((datetime.utcnow() + timedelta(days=5)) - datetime(year=1970, month=1, day=1)).total_seconds()),
            'data': {
                'cookies': cookies
            }
        }
        # Save the "NFAuthentication.key" file
        save_data(data, pin)
        # Close the browser
        self.ws_request('Browser.close')
        show_msg('Operations completed!\r\nThe "NFAuthentication.key" file has been saved in current folder.\r\nYour PIN protection is: {}'.format(pin),
                 TextFormat.COL_BLUE)

    def get_browser_debug_endpoint(self):
        start_time = time.time()
        while time.time() - start_time < 15:
            try:
                endpoint = ''
                data = urlopen('http://{0}:{1}/json'.format(LOCALHOST_ADDRESS, DEBUG_PORT), timeout=1).read().decode('utf-8')
                if not data:
                    raise ValueError
                session_list = json.loads(data)
                for item in session_list:
                    if item['type'] == 'page':
                        endpoint = item['webSocketDebuggerUrl']
                if not endpoint:
                    raise Warning('Chrome session page not found')
                self._ws = websocket.create_connection(endpoint)
                return
            except (URLError, socket.timeout, ValueError):  # json.JSONDecodeError inherited ValueError and available from >= py3.5
                pass
        raise Warning('Unable to connect with the browser')

    def wait_user_logged(self):
        start_time = time.time()
        while time.time() - start_time < 300:  # 5 min
            history_data = self.ws_request('Page.getNavigationHistory')
            history_index = history_data['currentIndex']
            # If the current page url is like "https://www.n*****x.com/browse" means that the user should have logged in successfully
            if '/browse' in history_data['entries'][history_index]['url']:
                return True
        return False

    @property
    def msg_id(self):
        self._msg_id += 1
        return self._msg_id

    @msg_id.setter
    def msg_id(self, value):
        self._msg_id = value

    def ws_request(self, method, params=None):
        req_id = self.msg_id
        message = json.dumps({'id': req_id, 'method': method, 'params': params or {}})
        self._ws.send(message)
        start_time = time.time()
        while True:
            if time.time() - start_time > 10:
                break
            message = self._ws.recv()
            parsed_message = json.loads(message)
            if 'result' in parsed_message and parsed_message['id'] == req_id:
                return parsed_message['result']
        raise Warning('No data received from browser')

    def ws_wait_event(self, method):
        start_time = time.time()
        while True:
            if time.time() - start_time > 10:
                break
            message = self._ws.recv()
            parsed_message = json.loads(message)
            if 'method' in parsed_message and parsed_message['method'] == method:
                return parsed_message
        raise Warning('No event data received from browser')


# Helper methods
class TextFormat:
    """Terminal color codes"""
    COL_BLUE = '\033[94m'
    COL_GREEN = '\033[92m'
    COL_LIGHT_YELLOW = '\033[93m'
    COL_LIGHT_RED = '\033[91m'
    COL_LIGHT_BLUE = '\033[94m'
    BOLD = '\033[1m'
    UNDERLINE = '\033[4m'
    END = '\033[0m'


def open_browser(browser_temp_path):
    params = ['-incognito',
              '--user-data-dir={}'.format(browser_temp_path),
              '--remote-debugging-port={}'.format(DEBUG_PORT),
              '--no-first-run',
              '--no-default-browser-check']
    dev_null = open(os.devnull, 'wb')
    try:
        return subprocess.Popen([get_browser_path()] + params, stdout=dev_null, stderr=subprocess.STDOUT)
    finally:
        dev_null.close()


def get_browser_path():
    """Check and return the name of the installed browser"""
    if '*' not in BROWSER_PATH:
        return BROWSER_PATH
    if IS_MACOS:
        for browser_name in ['Google Chrome', 'Chromium']:
            path = '/Applications/' + browser_name + '.app/Contents/MacOS/' + browser_name
            if os.path.exists(path):
                return path
    else:
        for browser_name in ['google-chrome', 'google-chrome-stable', 'google-chrome-unstable', 'chromium']:
            try:
                path = subprocess.check_output(['which', browser_name]).decode('utf-8').strip()
                if path:
                    return path
            except subprocess.CalledProcessError:
                pass
    raise Warning('Chrome or Chromium browser not detected.\r\nTry check if it is installed or specify the path in the BROWSER_PATH field inside "NFAuthenticationKey.py" file')


def assert_cookies(cookies):
    if not cookies:
        raise Warning('Not found cookies')
    login_cookies = ['memclid', 'nfvdid', 'SecureNetflixId', 'NetflixId']
    for cookie_name in login_cookies:
        if not any(cookie['name'] == cookie_name for cookie in cookies):
            raise Warning('Not found cookies')


def save_data(data, pin):
    raw = bytes(Padding.pad(data_to_pad=json.dumps(data).encode('utf-8'), block_size=16))
    iv = '\x00' * 16
    cipher = AES.new((str(pin) + str(pin) + str(pin) + str(pin)).encode('utf-8'), AES.MODE_CBC, iv.encode('utf-8'))
    encrypted_data = base64.b64encode(cipher.encrypt(raw)).decode('utf-8')
    file = open('NFAuthentication.key', 'w')
    file.write(encrypted_data)
    file.close()


def show_msg(text, text_format=None):
    if text_format:
        text = text_format + text + TextFormat.END
    print(text)


def input_msg(text, text_format=None):
    if text_format:
        text = text_format + text + TextFormat.END
    if sys.version_info.major == 2:
        return raw_input(text)
    else:
        return input(text)


if __name__ == '__main__':
    temp_path = tempfile.mkdtemp()
    try:
        Main(temp_path)
    except KeyboardInterrupt:
        show_msg('\r\nOperations cancelled')
    finally:
        try:
            shutil.rmtree(temp_path)
        except Exception:
            pass
