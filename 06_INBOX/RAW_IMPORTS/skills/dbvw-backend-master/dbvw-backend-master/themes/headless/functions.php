<?php

/**
 * Table of Contents ( rooted at ./library/ )
 *
 * [1]. globals.php – defines independent, globally available php functions
 * [2] abstracts – abstract classes that build common structure
 * [2.1]. abstracts/class-abstract-action-set.php – a class that scaffolds the definition of action sets encapsulating functionality
 * [2.2]. abstracts/class-abstract-filter-set.php– a class that scaffolds the definition of filter sets encapsulating functionality
 * [3] classes – classes that encapsulate site functionality and site actions.
 * [3.1] classes/class-init-actions.php, a class that initializes state via actions on theme-load.
 * [3.2] classes/class-init-actions.php, a class that sets up init-specific filters on theme-load.
 *
 */

/* [1] */
require_once('library/globals.php');

/* [2] */
require_once('library/abstracts/abstract_action_set.php');
require_once('library/abstracts/abstract_filter_set.php');

/* [3] */
require_once( 'library/classes/class_init_actions.php' );
require_once( 'library/classes/class_init_filters.php');

require_once( 'library/custom_dashboard_setup.php');

require_once( 'library/search_augmentation.php');

//keep this route alive during development
require_once( 'library/related_projects.php');

//changed to custom_related_projects 11-27-17, instead of related projects by category
require_once( 'library/custom_related_projects.php');


?>