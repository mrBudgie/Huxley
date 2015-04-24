﻿/*
Huxley - a JSON proxy for the UK National Rail Live Departure Board SOAP API
Copyright (C) 2015 James Singleton
 * http://huxley.unop.uk
 * https://github.com/jpsingleton/Huxley

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU Affero General Public License as published
by the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Affero General Public License for more details.

You should have received a copy of the GNU Affero General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Web.Http;
using Huxley.Models;
using Huxley.ldbServiceReference;

namespace Huxley.Controllers {
    public class DelaysController : BaseController {
        // GET /delays/CRS?accessToken=[your token]
        public async Task<DelaysResponse> Get([FromUri] StationBoardRequest request) {

            var londonTerminals = new List<string> { "BFR", "LBG", "CST", "CHX", "EUS", "FST", "KGX", "LST", "MYB", "PAD", "STP", "SPX", "VIC", "WAT", "WAE", };

            var client = new LDBServiceSoapClient();

            // Avoiding Problems with the Using Statement in WCF clients
            // https://msdn.microsoft.com/en-us/library/aa355056.aspx
            try {
                var totalDelayMinutes = 0;
                var totalTrainsDelayed = 0;

                dynamic config = new Formo.Configuration();
                int delayMinutesThreshold = config.DelayMinutesThreshold<int>(5);

                var token = MakeAccessToken(request.AccessToken);

                var filterCrs = request.FilterCrs;
                if (request.FilterCrs.Equals("LON", StringComparison.InvariantCultureIgnoreCase) ||
                    request.FilterCrs.Equals("London", StringComparison.InvariantCultureIgnoreCase)) {
                    filterCrs = null;
                }

                var board = await client.GetDepartureBoardAsync(token, request.NumRows, request.Crs, filterCrs, request.FilterType, 0, 0);

                var response = board.GetStationBoardResult;
                var filterLocationName = response.filterLocationName;

                var trainServices = response.trainServices;
                if (null == filterCrs) {
                    trainServices = trainServices.Where(ts => ts.destination.Any(d => londonTerminals.Contains(d.crs.ToUpperInvariant()))).ToArray();
                    filterCrs = "LON";
                    filterLocationName = "London";
                }

                // Parse the response from the web service.
                foreach (var si in trainServices.Where(si => !si.etd.Equals("On time", StringComparison.InvariantCultureIgnoreCase))) {
                    if (si.etd.Equals("Cancelled", StringComparison.InvariantCultureIgnoreCase)) {
                        totalTrainsDelayed++;
                    } else {
                        DateTime etd;
                        // Could be "Starts Here" or contain a *
                        if (DateTime.TryParse(si.etd.Replace("*", ""), out etd)) {
                            DateTime std;
                            if (DateTime.TryParse(si.std, out std)) {
                                var late = etd.Subtract(std);
                                totalDelayMinutes += (int)late.TotalMinutes;
                                if (late.TotalMinutes > delayMinutesThreshold) {
                                    totalTrainsDelayed++;
                                }
                            }
                        }
                    }
                }

                return new DelaysResponse {
                    GeneratedAt = response.generatedAt,
                    Crs = response.crs,
                    LocationName = response.locationName,
                    Filtercrs = filterCrs,
                    FilterLocationName = filterLocationName,
                    Delays = totalTrainsDelayed > 0,
                    TotalTrainsDelayed = totalTrainsDelayed,
                    TotalDelayMinutes = totalDelayMinutes,
                    TotalTrains = trainServices.Length,
                };

            } catch (CommunicationException) {
                client.Abort();
            } catch (TimeoutException) {
                client.Abort();
            } catch (Exception) {
                client.Abort();
                throw;
            } finally {
                client.Close();
            }
            return new DelaysResponse();
        }
    }
}