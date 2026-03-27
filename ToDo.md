
# Performance optimisations:
 - ~~Background services in API for:~~
   - ~~Signaling (use channels).~~ **Resolution**: Not required, performance is fine for awaiting~
   - ~~Db clean up schedule.~~ **Resolution**: Replaced with Channel based background service that clean history after changePixel calls.
 - ~~Control scoped dependencies (maybe PooledDbContext).~~ **Resolution**: Not needed now.
 - Redundant SignalR subscribing on canvasPage
 - ~~Profile performance to narrow down bottlenecks. **Resolution**: Performance is good for now.~~
# 1.0 Required Features
### Business Features:
- FAQ
- Versioning including logging
- Online users list
- List of pixel changes
- ~~Updating session Timeout on user activity **Resolution**: Done~~
### Infrastructure:
- ~~Db Cleanup.~~ **Resolution**: Done
- ~~Ensure Docker signaling.~~ **Resolution**: Done
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
 - docker/windows password hashes doesn't match, probably due to /r/n or trimming or smth.
 - unsubscribe button misplaced for long canvas names
 - Parameters Set is called more frequiently than expected for CanvasManager
# 1.1 Features
 - Canvas modes