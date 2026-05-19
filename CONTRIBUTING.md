# Contributing

## Guidelines
- Ensure email sending uses TLS by default. Set `EnableSsl` to `true` in `SmtpClient` unless a secure alternative is explicitly required and documented.

## Standards
- Avoid clear-text protocols for any outbound integrations that may carry credentials or sensitive data.