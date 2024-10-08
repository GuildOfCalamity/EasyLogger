![Icon](AppIcon.png) 
# EasyLogger



## v1.0.0.0 - August 2024
**Dependencies**

| Assembly | Version |
| ---- | ---- |
| NET Core | 6.0 (LTS) |
| NET Framework | 4.8.1 |

- A logger library which offers deferred writing so as to not block the main execution thread.
- There are seven flavors offered:
	- **Deferred Logger**
    	- A thread created for each write request via SemaphoreSlimEx.
	- **Buffered Logger**
    	- Threshold-based where tasks are created that access a ConcurrentQueue with a Timer.
	- **Queued Logger**
    	- A background, low priority thread monitors a BlockingCollection.
	- **Token Logger**
    	- A task monitors a BlockingCollection via GetConsumingEnumerable.
	- **WaitHandle Logger**
    	- A traditional EventWaitHandle setup with one background thread accessing a Queue.
	- **Interval Logger**
    	- A background thread monitors a BlockingCollection with last-write interval checking.
	- **HashSet Logger**
    	- A background thread monitors a HashSet for cutting down on duplicate entries.

- The final library is small, only **26 KB**. You could make it smaller by building with only the logger you prefer.
- This solution includes a console application for testing the logger DLL.
- I've added a custom [SemaphoreSlim](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim?view=netframework-4.8.1) for determining when it has been disposed. Ironically the BCL **SemaphoreSlim** does contain a **CheckDisposed()** method, but it's private.
	- From the [Dispose](https://learn.microsoft.com/en-us/dotnet/api/system.threading.semaphoreslim.dispose?view=netframework-4.8.1) notes: "*Always call Dispose before you release your last reference to the SemaphoreSlim. Otherwise, the resources it is using will not be freed until the garbage collector calls the SemaphoreSlim object's Finalize method.*"

![Screenshot](Screenshot.png)

- Selecting your preferred version in the IDE: 

![Framework](FrameworkSelection.png)