# Public release incident response

Stop scheduled posts and new distribution submissions when a credential leak,
P0 security issue, or user-harm risk is suspected. Revoke or rotate affected
credentials immediately; making a repository private or deleting an asset is
not a substitute.

The incident owner decides whether to unpublish a release or temporarily make
the repository private. Do not replace an asset at the same version. Record the
affected version, timeline, channels, corrective version, and completed notices
in an owner-private record. Public updates must omit secret values, victim data,
and unverified causes. History rewrite requires a separate backup, impact,
re-clone, and rollback plan plus explicit approval.
