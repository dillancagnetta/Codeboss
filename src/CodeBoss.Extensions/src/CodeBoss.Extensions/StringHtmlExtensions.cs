using System;
using System.Web;

namespace CodeBoss.Extensions;

public static partial class Extensions
{
    /// <summary>
    /// Converts line endings ( CRLF or just LF ) to non-encoded HTML breaks &lt;br&gt;
    /// </summary>
    /// <param name="str">a string that contains CR LF</param>
    /// <returns>a string with CRLF replaced with HTML <code>br</code></returns>
    public static string ConvertCrLfToHtmlBr( this string str )
    {
        if ( str == null )
        {
            return string.Empty;
        }

        // Normalize line breaks so this works with either CRLF or LF line endings.
        var result = str.Replace( "\r\n", "\n" );

        return result.Replace( "\n", "<br>" );
    }
    
    /// <summary>
    /// HTML Encodes the string.
    /// </summary>
    /// <param name="str">The string.</param>
    /// <returns></returns>
    public static string EncodeHtml( this string str )
    {
        return HttpUtility.HtmlEncode( str );
    }
    
    /// <summary>
    /// URLs the encode.
    /// </summary>
    /// <param name="str">The string.</param>
    /// <returns></returns>
    public static string UrlEncode( this string str )
    {
        return Uri.EscapeDataString( str );
    }
}