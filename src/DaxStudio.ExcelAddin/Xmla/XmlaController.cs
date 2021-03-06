﻿using System.Web.Http;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.IO;
using System;
using Serilog;
using System.Globalization;
using System.Xml;

namespace DaxStudio.ExcelAddin.Xmla
{
    [RoutePrefix("xmla")]
    public class XmlaController : ApiController
    {

        [HttpPost]
        [Route("")]
        public async Task<HttpResponseMessage> PostRawBufferManual()
        {
            string connStr = "";
            string loc = "";
            try
            {
                string request = await Request.Content.ReadAsStringAsync();

                var addin = Globals.ThisAddIn;
                var app = addin.Application;
                var wb = app.ActiveWorkbook;
                loc = wb.FullName;  //@"D:\Data\Presentations\Drop Your DAX\demos\02 DAX filter similar.xlsx";

                // parse request looking for workbook name in Workstation ID
                // we are using the Workstation ID property to tunnel the location property through
                // from the UI to the PowerPivot engine. The Location property does not appear to get
                // serialized through into the XMLA request so we "hijack" the Workstation ID
                var wsid = ParseRequestForWorkstationID(request);
                if (!string.IsNullOrEmpty(wsid)) loc = wsid;

                connStr = string.Format("Provider=MSOLAP;Persist Security Info=True;Initial Catalog=Microsoft_SQLServer_AnalysisServices;Data Source=$Embedded$;MDX Compatibility=1;Safety Options=2;MDX Missing Member Mode=Error;Subqueries=0;Optimize Response=7;Location=\"{0}\"", loc);
                //connStr = string.Format("Provider=MSOLAP;Persist Security Info=True;Data Source=$Embedded$;MDX Compatibility=1;Safety Options=2;MDX Missing Member Mode=Error;Subqueries=0;Optimize Response=7;Location={0}", loc);
                // 2010 conn str
                //connStr = string.Format("Provider=MSOLAP.5;Persist Security Info=True;Initial Catalog=Microsoft_SQLServer_AnalysisServices;Data Source=$Embedded$;MDX Compatibility=1;Safety Options=2;ConnectTo=11.0;MDX Missing Member Mode=Error;Optimize Response=3;Cell Error Mode=TextValue;Location={0}", loc);
                //connStr = string.Format("Provider=MSOLAP;Data Source=$Embedded$;MDX Compatibility=1;Safety Options=2;ConnectTo=11.0;MDX Missing Member Mode=Error;Optimize Response=3;Location={0};", loc);
                
                
                AmoWrapper.AmoType amoType = AmoWrapper.AmoType.AnalysisServices;
                if (float.Parse(app.Version,CultureInfo.InvariantCulture) >= 15)
                {
                    amoType = AmoWrapper.AmoType.Excel;
                    Log.Debug("{class} {method} {message}", "XmlaController", "PostRawBufferManual", "Loading Microsoft.Excel.Amo");
                }
                else
                {
                    Log.Debug("{class} {method} {message}", "XmlaController", "PostRawBufferManual", "defaulting to Microsoft.AnalysisServices");
                }

                var svr = new AmoWrapper.AmoServer(amoType);
                svr.Connect(connStr);

                // STEP 1: send the request to server.
                Log.Verbose("{class} {method} request: {request}", "XmlaController", "PostRawBufferManual", request);
                System.IO.TextReader streamWithXmlaRequest = new StringReader(request);
                
                System.Xml.XmlReader xmlaResponseFromServer=null; // will be used to parse the XML/A response from server
                string fullEnvelopeResponseFromServer = "";
                try
                {
                    //xmlaResponseFromServer = svr.SendXmlaRequest( XmlaRequestType.Undefined, streamWithXmlaRequest);
                    xmlaResponseFromServer = svr.SendXmlaRequest(streamWithXmlaRequest);
                    // STEP 2: read/parse the XML/A response from server.
                    xmlaResponseFromServer.MoveToContent();
                    fullEnvelopeResponseFromServer = xmlaResponseFromServer.ReadOuterXml();
                }
                catch (Exception ex)
                {
                    Log.Error("ERROR sending response: {class} {method} {exception}", "XmlaController", "PostRawBufferManual", ex);
                    //result = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    //result.Content = new StringContent(String.Format("An unexpected error occurred (sending XMLA request): \n{0}", ex.Message));
                }
                finally
                {
                    streamWithXmlaRequest.Close();
                }

                HttpResponseMessage result;
                try
                {
                    result = new HttpResponseMessage(HttpStatusCode.OK);
                    result.Content = new StringContent(fullEnvelopeResponseFromServer);

                    result.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
                    result.Headers.TransferEncodingChunked = true;
                }
                catch (Exception ex)
                {
                    Log.Error("ERROR sending response: {class} {method} {exception}", "XmlaController", "PostRawBufferManual", ex);
                    result = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                    result.Content = new StringContent(String.Format("An unexpected error occurred (reading XMLA response): \n{0}", ex.Message));
                }
                finally
                {
                // STEP 3: close the System.Xml.XmlReader, to release the connection for future use.
                    if (xmlaResponseFromServer != null)
                    {
                        xmlaResponseFromServer.Close();
                    }
                }
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("ERROR Connecting: {class} {method} loc: '{loc}' conn:'{connStr}' ex: {exception}", "XmlaController", "PostRawBufferManual", loc, connStr, ex);
                var expResult = new HttpResponseMessage(HttpStatusCode.InternalServerError);
                expResult.Content = new StringContent(String.Format("An unexpected error occurred: \n{0}", ex.Message));
                return expResult;
            }

        }

        private string ParseRequestForWorkstationID(string request)
        {
            string wsid = string.Empty;
            using (var sr = new StringReader(request))
            {
                using (var xmlRdr = new XmlTextReader(sr))
                {
                    while (xmlRdr.Read())
                    {
                        if (xmlRdr.NodeType == XmlNodeType.Element && xmlRdr.LocalName == "SspropInitWsid")
                        {
                            wsid = xmlRdr.ReadElementContentAsString();
                            return wsid;
                        }
                    }
                }
            }
            return wsid;
        }
    }
    
}