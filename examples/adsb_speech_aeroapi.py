import logging
import json
import os
import http.client

class AdsbSpeech:
    def __init__(self, 
                 log=logging.getLogger('adsb_speech'),
                 local_city="",
                 skip_general_aviation=False,
                 aeroapi_key=None,
                 aircraft_dir=None,
                 flight_cache_dir='/var/tmp/flights'):
        self.log = log
        self.flight_origin_dest_type = {}
        self.exclude_icao = set()
        self.unknown_origin = 'Unknown'
        self._local_city = local_city
        self._aeroapi_key = aeroapi_key
        self._aircraft_dir = aircraft_dir
        self._flight_cache_dir = flight_cache_dir
        self._skip_general_aviation = skip_general_aviation

    @property
    def local_city(self):
        return self._local_city
    
    @local_city.setter
    def local_city(self, value):
        self._local_city = value

    @property
    def skip_general_aviation(self):
        return self._skip_general_aviation
    
    @skip_general_aviation.setter
    def skip_general_aviation(self, value):
        self._skip_general_aviation = value

    def get_icao(self, flight):
        return flight['icao']
    
    def get_nautical_miles(self, flight):
        return flight['nm']
    
    def get_latitude(self, flight):
        return flight['lat']
    
    def get_flight_text(self, flight):
        icao = self.get_icao(flight)
        ident = flight['flt']
        dir_deg = round(flight['dir'], 0)
        dir_deg = 0 if dir_deg == 360 else dir_deg
        heading = "".join([n + ' ' for n in str(dir_deg)])
        text0 = ''
        origin, dest, type = (None, None, None)
        if ident[0] != 'N': 
            origin, dest, type = self._get_flight_origin_dest_type(ident)
        if origin and (origin == self._local_city or origin == self.unknown_origin) and dest:
            text0 = f"to {dest}"
        elif origin and (dest == self._local_city or not dest):
            text0 = f"from {origin}"
        elif origin:
            text0 = f"from {origin} to {dest}"
        airline = None
        if not self._skip_general_aviation or ident[0] != 'N':
            airline_code = ident[:3]
            airline = self._get_airline_name(airline_code, short=True)
        text = f"{airline if airline else ident} {type} at {flight['alt']} heading {heading}{text0}"
        if self._skip_general_aviation and ident[0] == 'N':
            # Drop general aviation tail #s
            skip_text = f"Skipping flight {text}"
            self.log.info(skip_text)
            self.exclude_icao.add(icao)
            return None
        self.log.info(f"FLIGHT {text}")
        return text

    def _get_aeroapi_jresult(self, url):
        headers = {'x-apikey' : self._aeroapi_key}
        self.log.debug(headers)
        h = http.client.HTTPSConnection('aeroapi.flightaware.com', timeout=1)
        h.request("GET", url, headers=headers)
        resp = h.getresponse().read().decode('utf-8')
        h.close()
        self.log.debug(resp)
        return json.loads(resp)

    def _get_aeroapi_flight(self, ident):
        self.log.warning(f'READING: AeroAPI flight info for {ident}')
        return self._get_aeroapi_jresult(f'/aeroapi/flights/{ident}')

    def _get_aeroapi_aircrafttype(self, aircraft_type):
        self.log.warning(f'READING: AeroAPI aircraft type info for {aircraft_type}')
        return self._get_aeroapi_jresult(f'/aeroapi/aircraft/types/{aircraft_type}')

    def _get_aeroapi_airlineinfo(self, airline_code):
        self.log.warning(f"READING: AeroAPI airline info for {airline_code}")
        return self._get_aeroapi_jresult(f'/aeroapi/operators/{airline_code}')

    def _write_flightinfoex(self, fn, ident, flightInfo):
        with open(fn, 'w+') as f:
            if 'error' in flightInfo:
                trimmedInfo = {{'flights':
                                [{'ident': ident,
                                    'aircraf_ttype': '',
                                    'origin': {'name': '', 'city': self._unknown_origin, 'code': ''},
                                    'destination': {'name': '', 'city': self._unknown_origin, 'code': ''}
                                }]}}
            else:
                flight0 = flightInfo['flights'][0]
                trimmedInfo = {'flights':
                                [{'ident': ident,
                                    'aircraft_type': flight0['aircraft_type'] if 'aircraft_type' in flight0 else '',
                                    'origin': {'code': flight0['origin']['code'], 'name': flight0['origin']['name'], 'city': flight0['origin']['city']},
                                    'destination': {'code': flight0['destination']['code'], 'name': flight0['destination']['name'], 'city': flight0['destination']['city']}}]}
            f.write(json.dumps(trimmedInfo))

    def _get_aircraft_type_name(self, aircrafttype):
        fn = os.path.join(self._aircraft_dir, f'aircraft/{aircrafttype}.json')
        jaircraft = None
        if os.path.isfile(fn):
            with open(fn) as f:
                aircraft = f.readline().strip()
                if not aircraft:
                    return None
                jaircraft = json.loads(aircraft)
        elif self._aeroapi_key:
            jaircraft = self._get_aeroapi_aircrafttype(aircrafttype)
            print(jaircraft)
            with open(fn, 'w') as fa:
                fa.write(json.dumps(jaircraft))
        else:
            return aircrafttype
        manufacturer = jaircraft['manufacturer']
        if not manufacturer:
            return None
        return f"{manufacturer} {jaircraft['type']}"

    def _get_airline_name(self, airline_code, short=False):
        fn = os.path.join(self._aircraft_dir, f'airline/{airline_code}.json')
        self.log.debug(fn)
        jairline = None
        if os.path.isfile(fn):
            with open(fn) as f:
                airline = f.readline().strip()
                if not airline:
                    return None
                jairline = json.loads(airline)
        elif self._aeroapi_key:
            jairline = self._get_aeroapi_airlineinfo(airline_code)
            if not jairline:
                return None
            print(jairline)
            with open(fn, 'w') as fa:
                fa.write(json.dumps(jairline))
        else:
            return None
        if 'name' not in jairline:
            return None
        shortname = jairline['shortname']
        name = shortname if shortname else jairline['name']
        country = jairline['country']
        if country and not short:
            return f"{name}, {country}"
        else:
            return f"{name}"

    def _parse_origin_dest_type(self, flightInfo, origin_dest_type):
        if 'error' in flightInfo:
            return
        flight = flightInfo['flights'][0]
        if 'origin' in flight:
            origin_dest_type[0] = flight['origin']['city'].split(',')[0].replace('/', ' ')
        if 'destination' in flight:
            origin_dest_type[1] = flight['destination']['city'].split(',')[0].replace('/', ' ')
        if 'aircraft_type' in flight:
            origin_dest_type[2] = flight['aircraft_type']

    def _get_flight_origin_dest_type(self, ident):
        origin_dest_type = [None, None, None]
        if ident in self.flight_origin_dest_type:
            return self.flight_origin_dest_type[ident]
        fn = os.path.join(self._flight_cache_dir, f'{ident}.json')
        try:
            if os.path.isfile(fn):
                self.log.debug(f'READING: Existing flight info for {ident}')
                with open(fn) as f:
                    flightFile = f.readline()
                    loadedFlight = json.loads(flightFile)
                    self._parse_origin_dest_type(loadedFlight, origin_dest_type)
                self.flight_origin_dest_type[ident] = origin_dest_type
            elif self._aeroapi_key:
                flightInfo = self._get_aeroapi_flight(ident)
                self.log.info(flightInfo)
                self._parse_origin_dest_type(flightInfo, origin_dest_type)
                self.flight_origin_dest_type[ident] = origin_dest_type
                try:
                    self._write_flightinfoex(fn, ident, flightInfo)
                except Exception as e:
                    self.log.exception(e)
        except Exception as e:
            self.log.exception(e)
        return origin_dest_type
