﻿using System;
using System.CodeDom;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using lightspeedAccess.Models.Configuration;
using System.Xml;
using System.Xml.Serialization;
using lightspeedAccess.Misc;
using lightspeedAccess.Models.Order;
using lightspeedAccess.Models.Request;

namespace lightspeedAccess.Services
{
	internal class WebRequestService
	{
		private readonly LightspeedConfig _config;

		public WebRequestService( LightspeedConfig config )
		{
			_config = config;
		}

		public T GetResponse< T >( LightspeedRequest request )
		{
			var webRequest = this.CreateHttpWebRequest( _config.Endpoint + request.GetUri() );
			
			var body = request.GetBody();
			if ( body != null )
			{
				webRequest.Method = "PUT";
				webRequest.ContentLength = body.Length;
				Stream dataStream = webRequest.GetRequestStream();
				dataStream.Write( body, 0, body.Length );
				dataStream.Close();

				webRequest.ContentType = "text/xml";
			}	

			var response = webRequest.GetResponse();
			var stream = response.GetResponseStream();

			var deserializer =
				new XmlSerializer( typeof( T ) );

			var result = ( T )deserializer.Deserialize( stream );

			var possibleAdditionalResponses = IterateThroughPagination< T >( request, result );

			var aggregatedResult = result as IPaginatedResponse;
			if( aggregatedResult != null )
				possibleAdditionalResponses.ForEach( resp => aggregatedResult.Aggregate( ( IPaginatedResponse )resp ) );

			return result;
		}

		public async Task<T> GetResponseAsync<T>( LightspeedRequest request )
		{
			var webRequest = this.CreateHttpWebRequest( _config.Endpoint + request.GetUri() );

			var body = request.GetBody();
			if ( body != null )
			{
				webRequest.Method = "PUT";
				webRequest.ContentLength = body.Length;
				Stream dataStream = webRequest.GetRequestStream();
				dataStream.Write( body, 0, body.Length );
				dataStream.Close();

				webRequest.ContentType = "text/xml";
			}	

			var response = await webRequest.GetResponseAsync();
			var stream = response.GetResponseStream();

			var deserializer =
				new XmlSerializer( typeof( T ) );

			var result = ( T )deserializer.Deserialize( stream );

			var possibleAdditionalResponses = await IterateThroughPaginationAsync< T >( request, result );

			var aggregatedResult = result as IPaginatedResponse;
			if( aggregatedResult != null )
				possibleAdditionalResponses.ForEach( resp => aggregatedResult.Aggregate( ( IPaginatedResponse )resp ) );

			return result;
		}

		private static bool NeedToIterateThroughPagination< T >( T response, LightspeedRequest r )
		{
			var paginatedResponse = response as IPaginatedResponse;
			var requestWithPagination = r as IRequestPagination;
			return ( paginatedResponse != null && requestWithPagination != null &&
			         paginatedResponse.GetCount() > requestWithPagination.GetLimit() );
		}

		private List< T > IterateThroughPagination< T >( LightspeedRequest r, T response )
		{
			var additionalResponses = new List< T >();

			if( !NeedToIterateThroughPagination( response, r ) )
				return additionalResponses;

			var paginatedRequest = ( IRequestPagination )r;
			var paginatedResponse = ( IPaginatedResponse )response;

			if( paginatedRequest.GetOffset() != 0 )
				return additionalResponses;

			var numPages = paginatedRequest.GetLimit() / paginatedResponse.GetCount() + 1;

			for( int pageNum = 1; pageNum < numPages; pageNum++ )
			{
				paginatedRequest.SetOffset( pageNum * 10 );
				additionalResponses.Add( GetResponse< T >( r ) );
			}

			return additionalResponses;
		}

		private async Task<List<T>> IterateThroughPaginationAsync<T>( LightspeedRequest r, T response )
		{
			var additionalResponses = new List<T>();

			if ( !NeedToIterateThroughPagination( response, r ) )
				return additionalResponses;

			var paginatedRequest = ( IRequestPagination ) r;
			var paginatedResponse = ( IPaginatedResponse ) response;

			if ( paginatedRequest.GetOffset() != 0 )
				return additionalResponses;

			var numPages = paginatedRequest.GetLimit() / paginatedResponse.GetCount() + 1;

			for ( int pageNum = 1; pageNum < numPages; pageNum++ )
			{
				paginatedRequest.SetOffset( pageNum * 10 );
			    additionalResponses.Add( await GetResponseAsync<T>( r ) );
			}

			return additionalResponses;
		}

		private HttpWebRequest CreateHttpWebRequest( string url )
		{
			LightspeedLogger.Log.Debug( "Composed lightspeed request URL: {0}", url );
			var uri = new Uri( url );
			var request = ( HttpWebRequest )WebRequest.Create( uri );
			
			request.Method = WebRequestMethods.Http.Get;
			request.Headers.Add( "Authorization", this.CreateAuthenticationHeader() );

			return request;
		}


		private string CreateAuthenticationHeader()
		{
			var authInfo = this._config.ApiKey == null ? string.Concat( this._config.Username, ":", this._config.Password ) : string.Concat( this._config.ApiKey, ":", "apikey" );  
			authInfo = Convert.ToBase64String( Encoding.Default.GetBytes( authInfo ) );

			return string.Concat( "Basic ", authInfo );
		}
	}
}