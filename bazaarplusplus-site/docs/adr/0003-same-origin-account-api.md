# Same-origin account requests

The browser sends account requests to the site's own origin so the account service can sign players in with an HttpOnly session cookie. The site Worker forwards the original request and response through a service binding to the server's named WebApi entrypoint. This preserves the browser Origin for POST validation and the Set-Cookie header for sign-in and sign-out. Preview and production use the same binding.

The server owns account routes, sessions and Run Summaries. ADR 0001 continues to govern public Hero Metrics Dataset requests, which the browser fetches directly from the metrics origin.
