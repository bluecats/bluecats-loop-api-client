﻿using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using BlueCats.Loop.Api.Client.Models;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BlueCats.Loop.Api.Client {

    /// <summary>
    /// Provides an async interface to the BlueCats Loop web API 
    /// </summary>
    public class LoopClient {

        /// <summary>
        /// Gets a value indicating whether this instance is authenticated.
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is authenticated; otherwise, <c>false</c>.
        /// </value>
        public bool IsAuthenticated => !string.IsNullOrEmpty( _authToken );

        private readonly HttpClient _client;
        private string _authToken;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopClient"/> class.
        /// </summary>
        /// <param name="baseUrl">The base URL.</param>
        public LoopClient( string baseUrl ) {
            if ( baseUrl == null ) throw new ArgumentNullException( nameof(baseUrl) );
            var baseUrl1 = new Uri( baseUrl );
            _client = new HttpClient { BaseAddress = baseUrl1 };
            _client.DefaultRequestHeaders.Accept.Clear();
            _client.DefaultRequestHeaders.Accept.Add( new MediaTypeWithQualityHeaderValue( "application/json" ) );
            _client.DefaultRequestHeaders.Add( "X-API-HEADER", "1" );
        }

        /// <summary>
        /// Authenticates with the Loop API asynchronously. This is required before making any other API calls.
        /// </summary>
        /// <param name="email">Loop user email.</param>
        /// <param name="password">Loop user password.</param>
        /// <exception cref="Exception">Received an empty auth token</exception>
        public async Task LoginAsync( string email, string password ) {
            if ( email == null ) throw new ArgumentNullException( nameof(email) );
            if ( password == null ) throw new ArgumentNullException( nameof(password) );
            // Request
            // Create json string from a C# Anonymous Class and JObject to serialize
            var json = JObject.FromObject( new { email, password } ).ToString();
            var jsonBytes = Encoding.ASCII.GetBytes( json );
            var content = new ByteArrayContent( jsonBytes );
            var route = new Uri( "login", UriKind.Relative );
            var request = _client.PostAsync( route, content );

            // Response
            using ( var response = await request.ConfigureAwait( false ) ) {
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
                var jsonObj = JObject.Parse( json );
                _authToken = (string) jsonObj["auth"];
                if ( string.IsNullOrEmpty( _authToken ) ) {
                    throw new Exception( "Received an empty auth token" );
                }
            }
        }

        /// <summary>
        /// Gets the paginated Loop Events asynchronously.
        /// </summary>
        /// <param name="objectType">Type of the Loop Object.</param>
        /// <param name="objectID">The Loop Object identifier.</param>
        /// <param name="eventType">Type of the Loop Event.</param>
        /// <param name="lastKeyID">The identifier for the key of the page to start on.</param>
        /// <param name="lastKeyTimestamp">The timestamp of the page to start on.</param>
        /// <param name="limit">The Page limit.</param>
        /// <param name="startTime">The window start time.</param>
        /// <param name="endTime">The window end time.</param>
        /// <returns>The paginated events</returns>
        public async Task< PaginatedEvents > GetPaginatedEventsAsync( string objectType, string objectID, string eventType = null, string lastKeyID = null, DateTime? lastKeyTimestamp = null, int? limit = null, DateTime? startTime = null, DateTime? endTime = null ) {
            if ( objectType == null ) throw new ArgumentNullException( nameof(objectType) );
            if ( objectID == null ) throw new ArgumentNullException( nameof(objectID) );
            EnsureAuthenticated();

            // Request
            const string ROUTE = "events";
            var queryStr = QueryHelpers.AddQueryString( ROUTE, nameof( objectType ), objectType );
            queryStr = QueryHelpers.AddQueryString( queryStr, nameof( objectID ), objectID );
            if ( !string.IsNullOrEmpty( eventType ) ) {
                queryStr = QueryHelpers.AddQueryString( queryStr, nameof( eventType ), eventType );
            }
            if ( !string.IsNullOrEmpty( lastKeyID ) ) {
                queryStr = QueryHelpers.AddQueryString( queryStr, nameof( lastKeyID ), lastKeyID );
            }
            if ( lastKeyTimestamp.HasValue ) {
                var timestamp = lastKeyTimestamp.Value.ToUniversalTime().ToString( Constants.TIMESTAMP_FORMAT );
                queryStr = QueryHelpers.AddQueryString( queryStr, "lastKeyTS", timestamp );
            }
            if ( limit.HasValue ) {
                queryStr = QueryHelpers.AddQueryString( queryStr, nameof( limit ), limit.ToString() );
            }
            if ( startTime.HasValue && endTime.HasValue) {
                var formattedTimestamp = startTime.Value.ToUniversalTime().ToString( Constants.TIMESTAMP_FORMAT );
                queryStr = QueryHelpers.AddQueryString( queryStr, "tsStart", formattedTimestamp );
                formattedTimestamp = endTime.Value.ToUniversalTime().ToString( Constants.TIMESTAMP_FORMAT );
                queryStr = QueryHelpers.AddQueryString( queryStr, "tsEnd", formattedTimestamp );
            }
            var uri = new Uri( queryStr, UriKind.Relative );
            var request = _client.GetAsync( uri );
            
            // Response
            using ( var response = await request.ConfigureAwait( false ) ) {
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
                var loopEvents = JsonConvert.DeserializeObject< PaginatedEvents >( json );
                return loopEvents;
            }
        }

        /// <summary>
        /// Posts Loop Events asynchronously.
        /// </summary>
        /// <param name="edgeMac">The MAC Address of the Edge Relay that generated the events.</param>
        /// <param name="eventInfos">The events to post.</param>
        /// <returns>The response string from the request</returns>
        public async Task< string > PostEventsAsync( string edgeMac, params EventInfo[] eventInfos ) {
            if ( edgeMac == null ) throw new ArgumentNullException( nameof(edgeMac) );
            if ( eventInfos == null ) throw new ArgumentNullException( nameof(eventInfos) );
            EnsureAuthenticated();

            // Request
            const string ROUTE = "events";
            var uri = new Uri( ROUTE, UriKind.Relative );
            var jObject = JObject.FromObject( new {
                edgeMAC = edgeMac,
                events = from e in eventInfos select e.EventData
            } );
            var json = JsonConvert.SerializeObject( jObject );
            var request = _client.PostAsync( uri, new StringContent( json, Encoding.ASCII, "application/json" ) );
            
            // Response
            return await UnwrapResponseStringAsync( request ).ConfigureAwait( false );
        }

        private async Task< string > UnwrapResponseStringAsync( Task< HttpResponseMessage > request ) {
            HttpResponseMessage response = null;
            try {
                response = await request.ConfigureAwait( false );
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync().ConfigureAwait( false );
                return content;
            }
            catch ( HttpRequestException ) {
                var builder = new StringBuilder();
                if ( response == null ) throw;
                builder.AppendLine( "[ Response ]" );
                builder.AppendLine( response.ToString() );
                throw new HttpRequestException( builder.ToString() );  
            }
            finally {
                response?.Dispose();
            }
        }

        private void EnsureAuthenticated() {
            if ( !IsAuthenticated ) throw new Exception( "Must login before API request" );
        }

    }

}