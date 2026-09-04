<?php
/**
 * Our custom dashboard page
 */

/** WordPress Administration Bootstrap */
require_once( ABSPATH . 'wp-load.php' );
require_once( ABSPATH . 'wp-admin/admin.php' );
require_once( ABSPATH . 'wp-admin/admin-header.php' );

$url = admin_url();
?>

<div class="wrap about-wrap">

	<h1>Welcome.</h1>

<!-- 	<div class="headless-instructions row">
		<div class="col-sm-6">
			<h3>Editing High Level Pages, such as the home page or about page:</h3>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=projects">Projects</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=people">People</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=news">News</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=about">Info Pages</a></h4>
		</div>
		<div class="col-sm-6">
			<h3>Editing Individual Items, such as projects, people, and news.</h3>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=projects">Home Page</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=people">About Page</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=news">Work Page</a></h4>
			<h4><a href="<?php echo $url; ?>edit.php?post_type=about">General Information</a></h4>
		</div>		
	</div> -->

</div>
<?php include( ABSPATH . 'wp-admin/admin-footer.php' );