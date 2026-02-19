
# Performance optimisations:
 - Background services in API for:
   - Signaling (use channels)
   - Db clean up schedule
 - Control scoped dependencies (maybe PooledDbContext)
 - Redundant SignalR subscribing on canvasPage
 - Profile to narrow down bottlenecks
# 1.0 Required Features
### Business Features:
- FAQ
- Versioning including logging
- Online users list
- List of pixel changes
- Updating session Timeout on user activity
### Infrastructure:
- Db Cleanup
- Ensure Docker signaling
- Disposing/closing of signalR connections
# 1.0 Wished Features
 - UI improvements
 - Google Auth
 - Canvas auto zoom on loading
 - Conway's Game of Life bot
 - Better home screen
 - Auto navigation improvements
# Technical debt:
 - Refactor
   - Remove redundant ports
   - Clean CanvasPage code
   - Improve logs and comments
   - Better error handling
   - Compile warnings
   - Runtime warnings and errors
# Bugs:
 - Edges of canvas can't reach edges of canvas component on some resolutions
 - Notification manager sometimes doesn't show notifications
# 1.1 Features
 - Canvas modes