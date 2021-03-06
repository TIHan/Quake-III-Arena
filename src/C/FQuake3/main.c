#include <m.h>

static gchar* mono_lib = "Mono\\lib";
static gchar* mono_etc = "Mono\\etc";

static GOptionEntry option_entries[] =
{
	{ "mono-lib", 0, 0, G_OPTION_ARG_STRING, &mono_lib, "Mono lib directory path", NULL },
	{ "mono-etc", 0, 0, G_OPTION_ARG_STRING, &mono_etc, "Mono etc directory path", NULL },
	{ NULL }
};

/*
==================
main
==================
*/
int
main (int argc, char *argv[])
{
	GOptionContext* option_context = g_option_context_new ("- options for mono");
	GError *error = NULL;
	MDomain* domain;
	
	g_option_context_add_main_entries (option_context, option_entries, NULL);

	if (!g_option_context_parse (option_context, &argc, &argv, &error))
	{
		g_print ("Invalid option: %s\n", error->message);
		return 1;
	}

	domain = m_domain_new (mono_lib, mono_etc, "Launcher.exe");

	// Load Core
	m_load_assembly ("FSharp.Core.dll");

	// Load Utils
	m_load_assembly ("FQuake3.Utils.dll");

	// Load Engine
	m_load_assembly ("Engine.dll");

	// Load Renderer
	m_load_assembly ("Engine.Renderer.dll");

	// Load CGame (temporary)
	m_load_assembly ("CGame.dll");

	m_domain_exec (domain, "Launcher.exe", argc, argv);

	// Free Domain
	m_domain_free (domain);

	// Free Option Context
	g_option_context_free (option_context);

	return 0;
}