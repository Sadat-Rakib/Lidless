// Source-level global usings so WPF markup compile (GenerateTemporaryTargetAssembly)
// sees System.IO / LINQ. SDK implicit usings are omitted here: UseWindowsForms would
// otherwise collide with WPF on Button, ComboBox, Brush, Cursors, MessageBox, etc.
global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
